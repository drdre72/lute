import json, urllib.request, time, re

def call(name, **args):
    req = json.dumps({
        'jsonrpc': '2.0', 'id': 1,
        'method': 'tools/call',
        'params': {'name': name, 'arguments': args}
    }).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=10)
    return json.loads(r.read().decode())

GO_ID = '16bbe875-188b-4683-ae75-be2fe0962312'

# Poll every 10s until close to market
for i in range(12):
    time.sleep(10)
    go = call('get_game_object', id=GO_ID)
    text = go['result']['content'][0]['text']
    pos = re.search(r'WorldPosition...([0-9.,-]+)', text)
    p = pos.group(1) if pos else "?"
    # Check distance to market center (15000, 15000)
    parts = [float(x) for x in p.split(',')]
    dist = ((parts[0]-15000)**2 + (parts[1]-15000)**2)**0.5
    print(f't={i*10+10}s pos=({parts[0]:.0f},{parts[1]:.0f},{parts[2]:.0f}) dist={dist:.0f} ({dist/39.37:.0f}m)')
    if dist < 500:
        print('Arrived at market!')
        break

# Read recent AgentDrive logs
console = call('read_console', since=0)
ctext = console['result']['content'][0]['text']
lines = ctext.split('\r\n')
drive_lines = [l for l in lines if 'AgentDrive' in l][-3:]
for l in drive_lines:
    print(l)
