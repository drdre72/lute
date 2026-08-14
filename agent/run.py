#!/usr/bin/env python3
"""CLI entry point for the Godot AI Pipeline agent.

Usage:
    python run.py "Create a Node3D root with a Camera3D child in res://scenes/test.tscn"
"""
from __future__ import annotations

import argparse
import sys

from agent import (
    DEFAULT_MODEL,
    LM_STUDIO_URL,
    OPENROUTER_DEFAULT_MODEL,
    OPENROUTER_FREE_MODEL,
    OPENROUTER_URL,
    run_agent,
    run_interactive,
)
from tools import GODOT_SERVER_URL


def main() -> int:
    parser = argparse.ArgumentParser(description="Drive the Godot editor via a local LLM.")
    parser.add_argument("task", nargs="?", default=None,
                         help="Natural-language description of what the agent should do. "
                              "Omit (or use --chat) to start an open-ended interactive session.")
    parser.add_argument("--chat", action="store_true", help="Start an open-ended interactive chat session.")
    parser.add_argument("--max-turns", type=int, default=20, help="Maximum agent loop iterations per message.")
    parser.add_argument("--model", default=DEFAULT_MODEL, help="Model name as loaded in LM Studio.")
    parser.add_argument("--lm-studio-url", default=LM_STUDIO_URL,
                         help="OpenAI-compatible chat completions endpoint (LM Studio by default; "
                              "point this at a cloud provider, e.g. https://openrouter.ai/api/v1/chat/completions, "
                              "to use a hosted model instead).")
    parser.add_argument("--godot-server-url", default=GODOT_SERVER_URL, help="AI Pipeline addon RPC endpoint.")
    parser.add_argument("--api-key", default="",
                         help="Bearer API key for cloud endpoints (falls back to the LLM_API_KEY env var). "
                              "Not needed for local LM Studio.")
    parser.add_argument("--cloud", action="store_true",
                         help=f"Shortcut for routing through OpenRouter ({OPENROUTER_URL}) with "
                              f"{OPENROUTER_DEFAULT_MODEL} instead of local LM Studio, unless --lm-studio-url "
                              "and/or --model are also given (those take precedence). Requires an API key "
                              "via --api-key or the LLM_API_KEY env var, and purchased OpenRouter credits.")
    parser.add_argument("--cloud-free", action="store_true",
                         help=f"Like --cloud, but defaults to {OPENROUTER_FREE_MODEL} "
                              "(no purchased credits required, but rate-limited).")
    parser.add_argument("--lean", action="store_true",
                         help="Skip injecting PRD.md and AGENT_RULES.md into the system prompt. "
                              "Saves ~2700 tokens of context for checklist-driven tasks.")
    parser.add_argument("--quiet", action="store_true", help="Suppress per-turn tool call logging.")
    args = parser.parse_args()

    if args.cloud or args.cloud_free:
        if args.lm_studio_url == LM_STUDIO_URL:
            args.lm_studio_url = OPENROUTER_URL
        if args.model == DEFAULT_MODEL:
            args.model = OPENROUTER_FREE_MODEL if args.cloud_free else OPENROUTER_DEFAULT_MODEL

    try:
        if args.chat or not args.task:
            run_interactive(
                max_turns_per_message=args.max_turns,
                model=args.model,
                lm_studio_url=args.lm_studio_url,
                godot_server_url=args.godot_server_url,
                verbose=not args.quiet,
                api_key=args.api_key,
                lean=args.lean,
            )
        else:
            run_agent(
                task=args.task,
                max_turns=args.max_turns,
                model=args.model,
                lm_studio_url=args.lm_studio_url,
                godot_server_url=args.godot_server_url,
                verbose=not args.quiet,
                api_key=args.api_key,
                lean=args.lean,
            )
    except Exception as exc:  # noqa: BLE001
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
