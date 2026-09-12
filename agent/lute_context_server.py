#!/usr/bin/env python3
"""Lute project-context MCP server.

Dependency-free stdio MCP server for Devin/Cascade-style agent sessions.
It deliberately exposes project knowledge as small, explicit tools instead
of dumping the entire repository into every prompt.

The server is repository-local: it reads/writes only the checked-out Lute
project and refuses paths outside the project root.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path
from typing import Any

ROOT = Path(os.environ.get("LUTE_PROJECT_ROOT", Path.cwd())).resolve()
MAX_READ = 120_000


def safe_path(relative: str) -> Path:
    p = (ROOT / relative).resolve()
    if p != ROOT and ROOT not in p.parents:
        raise ValueError("Path escapes the Lute project root")
    return p


def read_text(relative: str, limit: int = MAX_READ) -> str:
    p = safe_path(relative)
    if not p.is_file():
        return f"[missing file: {relative}]"
    text = p.read_text(encoding="utf-8", errors="replace")
    if len(text) > limit:
        return text[:limit] + "\n...[truncated]"
    return text


def section(text: str, heading: str) -> str:
    m = re.search(rf"^##+\s+{re.escape(heading)}\s*$", text, re.MULTILINE)
    if not m:
        return ""
    rest = text[m.end():]
    n = re.search(r"^##+\s+", rest, re.MULTILINE)
    return rest[: n.start() if n else len(rest)].strip()


def capability_lines() -> str:
    return read_text("LUTE_CAPABILITIES.md")


def search_project(query: str, max_results: int = 20) -> list[dict[str, Any]]:
    q = query.lower()
    results: list[dict[str, Any]] = []
    skip = {".git", ".sbox", "bin", "obj", "node_modules"}
    for path in ROOT.rglob("*"):
        if len(results) >= max_results:
            break
        if not path.is_file() or any(part in skip for part in path.parts):
            continue
        try:
            if path.stat().st_size > 400_000:
                continue
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        if q not in text.lower():
            continue
        lines = text.splitlines()
        hits = [i + 1 for i, line in enumerate(lines) if q in line.lower()]
        results.append({"path": str(path.relative_to(ROOT)), "lines": hits[:10]})
    return results


def remember(path: str, content: str, mode: str = "append") -> str:
    if path not in {".devin-context/memory.md", "LUTE_STATE.md"} and not path.startswith("docs/decisions/"):
        raise ValueError("Writes are restricted to memory, current state, and ADRs")
    p = safe_path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    if mode == "replace":
        p.write_text(content, encoding="utf-8")
    else:
        old = p.read_text(encoding="utf-8") if p.exists() else ""
        sep = "\n" if old and not old.endswith("\n") else ""
        p.write_text(old + sep + content.rstrip() + "\n", encoding="utf-8")
    return f"Updated {path}"


def tool_definitions() -> list[dict[str, Any]]:
    def t(name: str, description: str, props: dict[str, Any], required: list[str]) -> dict[str, Any]:
        return {
            "name": name,
            "description": description,
            "inputSchema": {"type": "object", "properties": props, "required": required},
        }

    return [
        t("lute_get_state", "Return the current Lute source of truth.", {}, []),
        t("lute_get_capabilities", "Return the capability registry and statuses.", {}, []),
        t("lute_get_rules", "Return the project rules relevant to agent behavior.", {}, []),
        t("lute_get_architecture", "Return the current technical architecture.", {}, []),
        t("lute_get_memory", "Return rolling session memory. Use current state for durable truth.", {}, []),
        t("lute_search", "Search repository text for a specific project concept, symbol, bug, or decision.",
          {"query": {"type": "string"}, "max_results": {"type": "integer", "default": 20}}, ["query"]),
        t("lute_read_file", "Read a repository file when targeted source context is needed.",
          {"path": {"type": "string"}}, ["path"]),
        t("lute_remember", "Persist a durable project-state note or ADR. Prefer updating state/capabilities through normal commits when changing canonical project truth.",
          {"path": {"type": "string"}, "content": {"type": "string"}, "mode": {"type": "string", "enum": ["append", "replace"], "default": "append"}}, ["path", "content"]),
    ]


def call_tool(name: str, args: dict[str, Any]) -> str:
    if name == "lute_get_state":
        return read_text("LUTE_STATE.md")
    if name == "lute_get_capabilities":
        return capability_lines()
    if name == "lute_get_rules":
        return read_text(".devin/rules/lute-core.md")
    if name == "lute_get_architecture":
        return read_text("ARCHITECTURE.md")
    if name == "lute_get_memory":
        return read_text(".devin-context/memory.md")
    if name == "lute_search":
        results = search_project(str(args.get("query", "")), int(args.get("max_results", 20)))
        return json.dumps(results, indent=2)
    if name == "lute_read_file":
        return read_text(str(args["path"]))
    if name == "lute_remember":
        return remember(str(args["path"]), str(args["content"]), str(args.get("mode", "append")))
    raise ValueError(f"Unknown tool: {name}")


def response(req_id: Any, result: Any = None, error: dict[str, Any] | None = None) -> None:
    obj: dict[str, Any] = {"jsonrpc": "2.0", "id": req_id}
    if error is not None:
        obj["error"] = error
    else:
        obj["result"] = result
    data = json.dumps(obj, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    sys.stdout.buffer.write(f"Content-Length: {len(data)}\r\n\r\n".encode("ascii"))
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()


def main() -> None:
    while True:
        headers: dict[str, str] = {}
        line = sys.stdin.buffer.readline()
        if not line:
            break
        while line not in (b"\r\n", b"\n", b""):
            try:
                k, v = line.decode("ascii").split(":", 1)
                headers[k.lower().strip()] = v.strip()
            except ValueError:
                pass
            line = sys.stdin.buffer.readline()
        length = int(headers.get("content-length", "0"))
        if length <= 0:
            continue
        raw = sys.stdin.buffer.read(length)
        try:
            req = json.loads(raw.decode("utf-8"))
            method = req.get("method")
            req_id = req.get("id")
            if method == "initialize":
                result = {
                    "protocolVersion": req.get("params", {}).get("protocolVersion", "2024-11-05"),
                    "capabilities": {"tools": {}},
                    "serverInfo": {"name": "lute-context", "version": "0.1.0"},
                }
                response(req_id, result)
            elif method == "notifications/initialized":
                continue
            elif method == "tools/list":
                response(req_id, {"tools": tool_definitions()})
            elif method == "tools/call":
                params = req.get("params", {})
                text = call_tool(params.get("name", ""), params.get("arguments", {}))
                response(req_id, {"content": [{"type": "text", "text": text}]})
            elif method == "ping":
                response(req_id, {})
            else:
                if req_id is not None:
                    response(req_id, error={"code": -32601, "message": f"Method not found: {method}"})
        except Exception as exc:
            if "req_id" in locals() and req_id is not None:
                response(req_id, error={"code": -32000, "message": str(exc)})


if __name__ == "__main__":
    main()
