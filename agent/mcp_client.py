#!/usr/bin/env python3
"""Shared MCP client for the S&Box editor's named-pipe transport.

The editor no longer serves MCP over HTTP (127.0.0.1:7269 is gone). Each
engine process now exposes a Windows named pipe named
`sbox-mcp-<pid>-<startTimeTicks:hex>` carrying newline-delimited UTF-8
JSON-RPC messages. This module discovers that pipe, performs the
`initialize` handshake once, and routes requests over a persistent
connection.

Usage from sibling scripts (run with `python agent\\<script>.py` so the
script dir is on sys.path):

    from mcp_client import mcp_call, call_tool
    result = mcp_call("tools/list")
    data = call_tool("find_game_objects", {"name": "Acropolis"})
"""
from __future__ import annotations

import json
import os
import sys
import time

_PIPE_ROOT = "//./pipe/"
_PIPE_PREFIX = "sbox-mcp-"
_PIPE_PATH_OVERRIDE = os.environ.get("SBOX_MCP_PIPE")  # e.g. //./pipe/sbox-mcp-1234-abcd

_pipe = None
_initialized = False
_request_id = 0


def list_pipes() -> list:
    """Return candidate sbox MCP pipe paths, newest editor first.

    Enumerates //./pipe/ for `sbox-mcp-<pid>-<ticks>` and orders by pid
    descending — a freshly restarted editor is the likeliest target.
    """
    if _PIPE_PATH_OVERRIDE:
        return [_PIPE_PATH_OVERRIDE]
    try:
        names = [n for n in os.listdir(_PIPE_ROOT) if n.startswith(_PIPE_PREFIX)]
    except OSError as e:
        raise RuntimeError(f"cannot enumerate {_PIPE_ROOT}: {e}")
    names.sort(
        key=lambda n: int(n.split("-")[2])
        if n.count("-") >= 3 and n.split("-")[2].isdigit() else 0,
        reverse=True,
    )
    return [_PIPE_ROOT + n for n in names]


def find_pipe() -> str:
    """Return the pipe path of the likeliest live sbox MCP endpoint."""
    candidates = list_pipes()
    if not candidates:
        raise RuntimeError(
            "no sbox-mcp pipe found — is the S&Box editor running with "
            "MCP enabled (Project Settings > AI > Enable MCP Communication)?"
        )
    return candidates[0]


def _connect():
    """(Re)open the pipe and perform the MCP initialize handshake.

    The engine creates a single pipe instance per connection; after a
    client disconnects there is a short gap before the next instance is
    listening, so open() is retried for a few seconds per candidate.
    """
    global _pipe, _initialized
    if _pipe is not None:
        try:
            _pipe.close()
        except OSError:
            pass
    candidates = list_pipes()
    if not candidates:
        raise RuntimeError(
            "no sbox-mcp pipe found — is the S&Box editor running with "
            "MCP enabled (Project Settings > AI > Enable MCP Communication)?"
        )
    last_err = None
    for _ in range(40):
        for path in candidates:
            try:
                _pipe = open(path, "r+b", buffering=0)
            except OSError as e:
                last_err = e
                continue
            _initialized = False
            try:
                _handshake()
            except Exception:
                try:
                    _pipe.close()
                except OSError:
                    pass
                _pipe = None
                raise
            return
        time.sleep(0.25)
    raise RuntimeError(f"sbox-mcp pipes exist but none accept connections: {last_err}")


def _send(payload: dict):
    line = (json.dumps(payload) + "\n").encode("utf-8")
    _pipe.write(line)


def _recv(expect_id, timeout: float = 60.0) -> dict:
    """Read newline-delimited messages until the response for expect_id arrives."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        line = _pipe.readline()
        if not line:
            raise RuntimeError("MCP pipe closed by server")
        try:
            msg = json.loads(line.decode("utf-8"))
        except json.JSONDecodeError:
            continue  # skip non-JSON noise
        if msg.get("id") == expect_id:
            return msg
        # otherwise a server notification — keep waiting
    raise TimeoutError(f"no MCP response for id={expect_id} within {timeout}s")


def _handshake():
    global _initialized, _request_id
    if _initialized:
        return
    _request_id += 1
    rid = _request_id
    _send({
        "jsonrpc": "2.0",
        "id": rid,
        "method": "initialize",
        "params": {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "lute-agent", "version": "1.0"},
        },
    })
    resp = _recv(rid)
    if "error" in resp:
        raise RuntimeError(f"MCP initialize failed: {resp['error']}")
    # Some servers require the initialized notification before tools/call.
    _send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    _initialized = True


def _ensure():
    if _pipe is None or not _initialized:
        _connect()


def mcp_call(method: str, params: dict | None = None, timeout: float = 60.0) -> dict:
    """Send a JSON-RPC request over the MCP pipe, return the result payload."""
    global _request_id
    for attempt in range(2):
        try:
            _ensure()
            _request_id += 1
            rid = _request_id
            payload = {"jsonrpc": "2.0", "id": rid, "method": method}
            if params is not None:
                payload["params"] = params
            _send(payload)
            resp = _recv(rid, timeout=timeout)
            if "error" in resp:
                raise RuntimeError(f"MCP error: {resp['error']}")
            return resp.get("result", {})
        except (OSError, BrokenPipeError, RuntimeError) as e:
            # Retry once after reconnect unless this was a server-side error.
            msg = str(e)
            if "MCP error" in msg or attempt == 1:
                raise
            _connect()
    return {}


def rpc_envelope(method: str, params: dict | None = None, timeout: float = 60.0) -> dict:
    """Send a JSON-RPC request and return the full response envelope.

    Unlike mcp_call (which unwraps result/error), this returns the raw
    {jsonrpc, id, result|error} dict — useful for legacy scripts that
    index r['result']['content'] themselves.
    """
    global _request_id
    for attempt in range(2):
        try:
            _ensure()
            _request_id += 1
            rid = _request_id
            payload = {"jsonrpc": "2.0", "id": rid, "method": method}
            payload["params"] = params if params is not None else {}
            _send(payload)
            return _recv(rid, timeout=timeout)
        except (OSError, BrokenPipeError, RuntimeError):
            if attempt == 1:
                raise
            _connect()
    return {}


def rpc_call_tool(name: str, arguments: dict | None = None, timeout: float = 60.0) -> dict:
    """tools/call variant returning the full envelope (legacy script compat)."""
    return rpc_envelope("tools/call", {"name": name, "arguments": arguments or {}}, timeout=timeout)


def call_tool(name: str, arguments: dict | None = None, timeout: float = 60.0) -> dict:
    """Call an MCP tool and unwrap the content[0].text payload as JSON if possible."""
    params = {"name": name}
    if arguments:
        params["arguments"] = arguments
    result = mcp_call("tools/call", params, timeout=timeout)
    if isinstance(result, dict):
        if "structuredContent" in result:
            return result["structuredContent"]
        for item in result.get("content", []):
            if item.get("type") == "text":
                try:
                    return json.loads(item["text"])
                except (json.JSONDecodeError, TypeError):
                    return {"_raw": item["text"]}
    return result


def close():
    global _pipe, _initialized
    if _pipe is not None:
        try:
            _pipe.close()
        except OSError:
            pass
    _pipe = None
    _initialized = False


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "status":
        print(json.dumps(call_tool("editor_status"), indent=2))
    else:
        print("pipe:", find_pipe())
