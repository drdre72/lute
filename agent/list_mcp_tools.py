"""List all MCP tools."""
from mcp_client import mcp_call

tools = mcp_call("tools/list")

for t in tools.get('tools', []):
    name = t.get('name', '')
    desc = t.get('description', '')[:100]
    print(f'{name}: {desc}')
