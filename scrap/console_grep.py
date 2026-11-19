import sys, re, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.path.insert(0, 'agent')
from mcp_client import call_tool

limit = int(sys.argv[1]) if len(sys.argv) > 1 else 500
include = sys.argv[2] if len(sys.argv) > 2 else 'Lute:'
exclude = sys.argv[3].split('|') if len(sys.argv) > 3 else ['ReservationConflict', 'claim rejected']

r = call_tool('read_console', {'limit': limit})
lines = r.get('Lines', r.get('_raw', ''))
if isinstance(lines, list):
    lines = '\n'.join(str(l) for l in lines)
out = [l for l in str(lines).split('\n') if include in l and not any(x in l for x in exclude)]
print('\n'.join(l[:175] for l in out))
