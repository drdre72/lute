"""Direct MCP JSON-RPC caller for S&Box editor.

Usage:
  python agent/mcp_call.py list_tools
  python agent/mcp_call.py call <tool_name> [json_args]
  python agent/mcp_call.py search <keyword>
"""
import sys
import json

from mcp_client import rpc_envelope as rpc

def list_tools():
    r = rpc("tools/list")
    if "result" in r and "tools" in r["result"]:
        for t in r["result"]["tools"]:
            name = t.get("name", "?")
            desc = t.get("description", "")[:80]
            print(f"  {name}: {desc}")
    else:
        print(json.dumps(r, indent=2))

def search(keyword):
    r = rpc("tools/list")
    if "result" in r and "tools" in r["result"]:
        kw = keyword.lower()
        for t in r["result"]["tools"]:
            name = t.get("name", "?")
            desc = t.get("description", "")
            if kw in name.lower() or kw in desc.lower():
                print(f"  {name}: {desc[:120]}")
    else:
        print(json.dumps(r, indent=2))

def call_tool(name, args_json):
    if not args_json or args_json == "FILE":
        import os
        base = os.path.dirname(os.path.abspath(__file__))
        path = os.path.join(base, "cmd_args.json")
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                args_json = f.read()
        except FileNotFoundError:
            args_json = "{}"
    args = json.loads(args_json) if args_json else {}
    r = rpc("tools/call", {"name": name, "arguments": args})
    if "result" in r:
        result = r["result"]
        if "content" in result:
            for c in result["content"]:
                if c.get("type") == "text":
                    print(c["text"])
                else:
                    print(json.dumps(c, indent=2))
        else:
            print(json.dumps(result, indent=2))
    else:
        print(json.dumps(r, indent=2))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    cmd = sys.argv[1]
    if cmd == "list_tools":
        list_tools()
    elif cmd == "search":
        search(sys.argv[2])
    elif cmd == "call":
        call_tool(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "{}")
    else:
        print(f"Unknown command: {cmd}")
        sys.exit(1)
