"""List all MCP tools."""
import json, urllib.request

MCP = 'http://127.0.0.1:7269/mcp'
req = json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'tools/list', 'params': {}}).encode()
r = urllib.request.urlopen(MCP, req, timeout=10)
tools = json.loads(r.read().decode())

for t in tools.get('tools', []):
    name = t.get('name', '')
    desc = t.get('description', '')[:100]
    print(f'{name}: {desc}')
