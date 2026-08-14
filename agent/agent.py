"""Agentic loop that lets a local LM Studio-hosted model (e.g. Qwen 2.5 14B)
drive the Godot editor through the AI Pipeline addon's HTTP tool server.
"""
from __future__ import annotations

import datetime
import json
import os
import re
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional

import requests

from tools import TOOL_SCHEMAS, call_tool, GODOT_SERVER_URL

LM_STUDIO_URL = "http://127.0.0.1:1234/v1/chat/completions"
DEFAULT_MODEL = "qwen2.5-coder-14b-instruct-mlx"
# API key for cloud/hosted OpenAI-compatible endpoints (OpenAI, OpenRouter,
# Anthropic-via-gateway, etc). Local LM Studio needs none. Falls back to the
# LLM_API_KEY env var if --api-key isn't passed on the CLI.
API_KEY_ENV_VAR = "LLM_API_KEY"

# OpenRouter: one API key, OpenAI-compatible schema, hosts most open-weight
# and proprietary models. Used when --cloud is passed to run.py without
# overriding --lm-studio-url/--model. Get a key at https://openrouter.ai/keys.
OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"
OPENROUTER_DEFAULT_MODEL = "deepseek/deepseek-chat"  # DeepSeek-V3 (paid, needs credits)
# No-cost fallback: OpenRouter's free-tier models are rate-limited but require
# no purchased credits. Verified to support tool/function calling.
OPENROUTER_FREE_MODEL = "nvidia/nemotron-3-ultra-550b-a55b:free"

# Context-window safety knobs for long/open-ended conversations.
MAX_TOOL_RESULT_CHARS = 4000
MAX_HISTORY_MESSAGES = 40  # excludes the pinned system message
MAX_CONTEXT_FILE_CHARS = 8000  # cap on PRD/rules content injected into context
MAX_PROGRESS_ENTRIES = 3  # how many recent progress-log entries to inject on wake
MAX_PROGRESS_ENTRY_WORDS = 100  # per-entry word cap when writing to the progress log
MAX_PROGRESS_CONTEXT_CHARS = 1500  # hard cap on total progress-log text injected on wake
LM_REQUEST_TIMEOUT_SECONDS = 600  # local models can be slow to process long prompts

PROJECT_ROOT = Path(__file__).resolve().parent.parent
PRD_PATH = PROJECT_ROOT / "PRD.md"
AGENT_RULES_PATH = PROJECT_ROOT / "AGENT_RULES.md"
PROGRESS_LOG_PATH = PROJECT_ROOT / "PROGRESS_LOG.md"

BASE_SYSTEM_PROMPT = (
    "You are an autonomous game-development agent with full control over a Godot 4 "
    "editor session through a set of tools. The game is called 'Lute' (C#/.NET, Godot "
    "4.7); the PRD below is the source of truth for what the game actually is. Use the "
    "tools to inspect the current scene tree before making changes, make incremental "
    "changes, and verify your work by re-reading state after mutations. Prefer res:// "
    "paths for all file references. When a task is complete, reply normally (without a "
    "tool call) summarizing what you did.\n\n"
    "For large multi-step build tasks, a checklist is often prepared for you in "
    "MetaList.txt. If the user's request references a checklist, or if "
    "checklist_status shows incomplete items, work through it one step at a time: call "
    "checklist_next to get exactly one item, execute the single tool call it describes "
    "(the values are pre-computed for you -- execute them exactly rather than "
    "re-deriving dimensions yourself), then call checklist_next again with "
    "completed_index set to the index you just finished to mark it done and get the next "
    "item in a single call. Do not try to plan or execute the whole checklist "
    "in one turn. Save the scene once checklist_next returns {'done': true}."
)


class AgentError(RuntimeError):
    pass


def _read_context_file(path: Path) -> str:
    if not path.exists():
        return ""
    text = path.read_text(encoding="utf-8", errors="ignore").strip()
    if len(text) > MAX_CONTEXT_FILE_CHARS:
        text = "...[earlier content truncated]...\n" + text[-MAX_CONTEXT_FILE_CHARS:]
    return text


def build_system_prompt(lean: bool = False) -> str:
    """Assemble the system prompt, injecting PRD.md, AGENT_RULES.md, and
    PROGRESS_LOG.md content so the agent has persistent project context on
    every wake.

    If lean=True, skips PRD.md and AGENT_RULES.md injection to save ~2700
    tokens of context. Use for checklist-driven tasks where the model only
    needs tool-call instructions, not full project lore."""
    parts = [BASE_SYSTEM_PROMPT]

    if lean:
        parts.append(
            "## Project\nLute — Godot 4.7 C#/.NET MMORPG. "
            "You are building the Temple of Time scene. "
            "Follow the checklist workflow exactly."
        )
    else:
        prd = _read_context_file(PRD_PATH)
        if prd:
            parts.append(f"## Project PRD ({PRD_PATH.name})\n{prd}")
        else:
            parts.append(f"## Project PRD\nNo {PRD_PATH.name} found. Ask the user for requirements if unclear.")

        rules = _read_context_file(AGENT_RULES_PATH)
        if rules:
            parts.append(f"## Agent Rules ({AGENT_RULES_PATH.name})\n{rules}")

    progress = _read_progress_tail()
    if progress:
        parts.append(
            f"## Progress Log ({PROGRESS_LOG_PATH.name}) — {MAX_PROGRESS_ENTRIES} most recent "
            f"successful iterations\n{progress}"
        )
    else:
        parts.append("## Progress Log\nNo prior progress recorded yet.")

    return "\n\n".join(parts)


def _read_progress_tail() -> str:
    """Return only the most recent MAX_PROGRESS_ENTRIES timeline lines from the
    progress log, so a long history doesn't bloat the prompt and slow down
    inference."""
    if not PROGRESS_LOG_PATH.exists():
        return ""
    lines = [
        ln for ln in PROGRESS_LOG_PATH.read_text(encoding="utf-8", errors="ignore").splitlines()
        if ln.startswith("- [")
    ]
    if not lines:
        return ""
    recent = lines[-MAX_PROGRESS_ENTRIES:]
    tail = "\n".join(recent)
    if len(tail) > MAX_PROGRESS_CONTEXT_CHARS:
        tail = "...[older entries truncated]...\n" + tail[-MAX_PROGRESS_CONTEXT_CHARS:]
    return tail


def _condense_to_words(text: str, max_words: int = MAX_PROGRESS_ENTRY_WORDS) -> str:
    """Collapse text to a single-line summary of at most max_words words
    (timestamp is not counted against this limit)."""
    flat = " ".join(text.split())
    words = flat.split(" ")
    if len(words) > max_words:
        return " ".join(words[:max_words]) + " ..."
    return flat


def _record_progress(user_summary: str, agent_summary: str) -> None:
    """Append a single condensed timeline entry to PROGRESS_LOG.md. Call only
    after a successful (non-erroring) iteration. Each entry is a one-line
    timeline item capped at MAX_PROGRESS_ENTRY_WORDS words total (excluding
    the timestamp): a short slice of the request plus most of the budget
    reserved for the actual outcome, so long task prompts don't crowd out
    what actually happened."""
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    request_words = max(15, MAX_PROGRESS_ENTRY_WORDS // 5)
    result_words = MAX_PROGRESS_ENTRY_WORDS - request_words
    short_request = _condense_to_words(user_summary, request_words)
    short_result = _condense_to_words(agent_summary, result_words)
    line = f"{short_request} -> {short_result}"
    write_header = not PROGRESS_LOG_PATH.exists()
    with PROGRESS_LOG_PATH.open("a", encoding="utf-8") as f:
        if write_header:
            f.write("# Lute AI Pipeline — Progress Log\n\n")
        f.write(f"- [{timestamp}] {line}\n")


def _truncate_tool_result(result: Any) -> str:
    text = json.dumps(result)
    if len(text) > MAX_TOOL_RESULT_CHARS:
        return text[:MAX_TOOL_RESULT_CHARS] + f"... [truncated, {len(text)} chars total]"
    return text


def _trim_history(messages: List[Dict[str, Any]]) -> None:
    """Keep the pinned system message plus the most recent MAX_HISTORY_MESSAGES
    messages, dropping the oldest turns in between. Mutates in place."""
    if not messages or messages[0].get("role") != "system":
        return
    system_msg, rest = messages[0], messages[1:]
    if len(rest) > MAX_HISTORY_MESSAGES:
        rest = rest[-MAX_HISTORY_MESSAGES:]
    messages[:] = [system_msg] + rest


def _chat_completion(
    messages: List[Dict[str, Any]], model: str, lm_studio_url: str, api_key: str = ""
) -> Dict[str, Any]:
    payload = {
        "model": model,
        "messages": messages,
        "tools": TOOL_SCHEMAS,
        "tool_choice": "auto",
        "temperature": 0.2,
    }
    headers = {"Authorization": f"Bearer {api_key}"} if api_key else {}
    resp = requests.post(lm_studio_url, json=payload, headers=headers, timeout=LM_REQUEST_TIMEOUT_SECONDS)
    if resp.status_code != 200:
        raise AgentError(f"Chat completions endpoint returned {resp.status_code}: {resp.text}")
    return resp.json()


def _extract_tool_calls_from_content(content: str) -> List[Dict[str, Any]]:
    """Fallback for local models (e.g. Qwen 7B) that put the tool call inside
    markdown JSON blocks instead of the OpenAI `tool_calls` field.
    Returns a list of synthetic tool_calls dicts, or an empty list."""
    if not content or not isinstance(content, str):
        return []

    # Try fenced JSON code block first, then fall back to the whole message
    block_match = re.search(r"```(?:json)?\s*([\s\S]*?)\s*```", content)
    text = block_match.group(1).strip() if block_match else content.strip()

    # Extract as many JSON objects as we can from the text.  Use raw_decode
    # so we handle multiple concatenated objects and arbitrary nesting depth.
    decoder = json.JSONDecoder()
    idx = 0
    objects: List[Dict[str, Any]] = []
    while idx < len(text):
        # Skip non-JSON leading noise
        try:
            while idx < len(text) and text[idx] not in "{[":
                idx += 1
            if idx >= len(text):
                break
            obj, end = decoder.raw_decode(text, idx)
            if isinstance(obj, dict):
                objects.append(obj)
            elif isinstance(obj, list):
                for item in obj:
                    if isinstance(item, dict):
                        objects.append(item)
            idx += end
        except (json.JSONDecodeError, ValueError):
            idx += 1

    tool_calls: List[Dict[str, Any]] = []
    for obj in objects:
        name = obj.get("name")
        arguments = obj.get("arguments", obj.get("parameters", obj.get("args", {})))
        if not name:
            continue
        if isinstance(arguments, dict):
            arguments = json.dumps(arguments)
        if isinstance(arguments, str):
            tool_calls.append({
                "id": f"call_{uuid.uuid4().hex[:8]}",
                "type": "function",
                "function": {"name": name, "arguments": arguments},
            })
    return tool_calls


def _run_until_response(
    messages: List[Dict[str, Any]],
    max_turns: int,
    model: str,
    lm_studio_url: str,
    godot_server_url: str,
    verbose: bool,
    api_key: str = "",
) -> str:
    """Drive tool calls (mutating `messages` in place) until the model replies
    with plain content instead of a tool call, or max_turns is exhausted."""
    last_checklist_index: Optional[int] = None
    last_tool_was_checklist = False

    for turn in range(1, max_turns + 1):
        _trim_history(messages)
        response = _chat_completion(messages, model, lm_studio_url, api_key)
        choice = response["choices"][0]
        message = choice["message"]

        tool_calls = message.get("tool_calls") or []
        if not tool_calls:
            # Some local models return tool calls as markdown JSON in content
            tool_calls = _extract_tool_calls_from_content(message.get("content", ""))
            if tool_calls:
                message["tool_calls"] = tool_calls
                # Keep content for the model's own reference, but the loop
                # will now process the extracted calls first.

        messages.append(message)

        if not tool_calls:
            return message.get("content", "")

        for tool_call in tool_calls:
            fn = tool_call["function"]
            name = fn["name"]
            try:
                args = json.loads(fn.get("arguments") or "{}")
            except json.JSONDecodeError:
                args = {}

            # Auto-inject completed_index for forgetful local models (e.g. Qwen 7B)
            # that call checklist_next after a non-checklist tool but omit completed_index.
            if name == "checklist_next" and "completed_index" not in args:
                if last_checklist_index is not None and not last_tool_was_checklist:
                    args["completed_index"] = last_checklist_index
                    if verbose:
                        print(f"[agent] auto-injected completed_index={last_checklist_index} into checklist_next")

            if verbose:
                print(f"[agent] turn {turn}: calling {name}({args})")

            result = call_tool(name, args, server_url=godot_server_url)

            if verbose:
                print(f"[agent] result: {result}")

            messages.append({
                "role": "tool",
                "tool_call_id": tool_call["id"],
                "name": name,
                "content": _truncate_tool_result(result),
            })

            # Track state for the next turn
            if name == "checklist_next":
                last_tool_was_checklist = True
                if isinstance(result, dict) and "index" in result:
                    last_checklist_index = result["index"]
            else:
                last_tool_was_checklist = False

    raise AgentError(f"Agent did not finish within {max_turns} turns.")


def run_agent(
    task: str,
    max_turns: int = 20,
    model: str = DEFAULT_MODEL,
    lm_studio_url: str = LM_STUDIO_URL,
    godot_server_url: str = GODOT_SERVER_URL,
    verbose: bool = True,
    api_key: str = "",
    lean: bool = False,
) -> str:
    """Run a single task to completion and return the model's final reply.
    Only appends to PROGRESS_LOG.md if the task completes without error."""
    api_key = api_key or os.environ.get(API_KEY_ENV_VAR, "")
    messages: List[Dict[str, Any]] = [
        {"role": "system", "content": build_system_prompt(lean=lean)},
        {"role": "user", "content": task},
    ]
    final_text = _run_until_response(messages, max_turns, model, lm_studio_url, godot_server_url, verbose, api_key)
    if verbose:
        print(f"\n[agent] Final response:\n{final_text}")
    _record_progress(f"Task: {task}", final_text)
    return final_text


def run_interactive(
    max_turns_per_message: int = 20,
    model: str = DEFAULT_MODEL,
    lm_studio_url: str = LM_STUDIO_URL,
    godot_server_url: str = GODOT_SERVER_URL,
    verbose: bool = True,
    api_key: str = "",
    lean: bool = False,
) -> None:
    """Open-ended chat loop. Conversation history persists across turns (up to
    MAX_HISTORY_MESSAGES), so the agent remembers earlier context. Type 'exit'
    or 'quit' to leave, '/reset' to clear history and start fresh."""
    api_key = api_key or os.environ.get(API_KEY_ENV_VAR, "")
    system_prompt = build_system_prompt(lean=lean)
    messages: List[Dict[str, Any]] = [{"role": "system", "content": system_prompt}]
    print("[agent] Interactive mode. Type 'exit' to quit, '/reset' to clear history.")
    ctx_label = "lean context" if lean else f"context from {PRD_PATH.name}, {AGENT_RULES_PATH.name}, and {PROGRESS_LOG_PATH.name}"
    print(f"[agent] Loaded {ctx_label}.")

    while True:
        try:
            user_input = input("\nyou> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\n[agent] Bye.")
            return

        if not user_input:
            continue
        if user_input.lower() in {"exit", "quit"}:
            print("[agent] Bye.")
            return
        if user_input == "/reset":
            messages = [{"role": "system", "content": system_prompt}]
            print("[agent] History cleared.")
            continue

        messages.append({"role": "user", "content": user_input})
        try:
            final_text = _run_until_response(
                messages, max_turns_per_message, model, lm_studio_url, godot_server_url, verbose, api_key
            )
        except AgentError as exc:
            print(f"[agent] error: {exc}")
            continue

        print(f"\nagent> {final_text}")
        _record_progress(f"User: {user_input}", final_text)
