#!/usr/bin/env python3
"""MCP server exposing the Godot AI Pipeline tools over stdio.

Configured for Windsurf at:
    ~/.codeium/windsurf/mcp_config.json

Run directly for local testing:
    python mcp_server.py
"""
from __future__ import annotations

import json
import logging
from typing import Any, Optional

import anyio
from mcp.server.lowlevel.server import Server
from mcp.server.stdio import stdio_server
from mcp.types import TextContent, Tool, ServerCapabilities, ToolsCapability

from tools import GODOT_SERVER_URL, TOOL_SCHEMAS, call_tool

logging.basicConfig(level=logging.WARNING)
logger = logging.getLogger("godot-ai-pipeline-mcp")


def _build_tools() -> list[Tool]:
    """Map the existing OpenAI-style TOOL_SCHEMAS to MCP Tool objects."""
    tools: list[Tool] = []
    for schema in TOOL_SCHEMAS:
        fn = schema["function"]
        params = fn.get("parameters", {})
        tools.append(
            Tool(
                name=fn["name"],
                description=fn["description"],
                inputSchema={
                    "type": "object",
                    "properties": params.get("properties", {}),
                    "required": params.get("required", []),
                },
            )
        )
    return tools


def _sync_call(name: str, args: dict) -> Any:
    """Synchronous wrapper to call the Godot AI Pipeline HTTP server."""
    return call_tool(name, args, server_url=GODOT_SERVER_URL)


def _result_to_text(result: Any) -> list[TextContent]:
    """Serialize tool results as JSON text content."""
    return [TextContent(type="text", text=json.dumps(result, default=str))]


async def main() -> None:
    server = Server("godot-ai-pipeline")
    tools = _build_tools()

    @server.list_tools()
    async def list_tools() -> list[Tool]:
        return tools

    @server.call_tool()
    async def handle_call_tool(name: str, arguments: dict | None) -> list[TextContent]:
        arguments = arguments or {}
        logger.info(f"tool call: {name}({arguments})")
        # Run the blocking HTTP request in a worker thread so it does not
        # stall the async stdio event loop.
        result = await anyio.to_thread.run_sync(_sync_call, name, arguments)
        logger.info(f"tool result: {result}")
        return _result_to_text(result)

    async with stdio_server() as (read_stream, write_stream):
        init_options = server.create_initialization_options(
            capabilities=ServerCapabilities(tools=ToolsCapability())
        )
        await server.run(
            read_stream,
            write_stream,
            init_options,
        )


if __name__ == "__main__":
    # Use the asyncio backend; the trio backend can deadlock with stdio
    # on some platforms when worker threads block on I/O.
    anyio.run(main, backend="asyncio")
