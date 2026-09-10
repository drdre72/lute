"""Restart play mode and check for the new build message."""
import json, urllib.request, time

def mcp_call(tool, args={}):
    payload = json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'tools/call', 'params': {'name': tool, 'arguments': args}})
    req = urllib.request.Request('http://127.0.0.1:7269/mcp', data=payload.encode(), headers={'Content-Type': 'application/json'})
    try:
        resp = urllib.request.urlopen(req, timeout=15)
        return json.loads(resp.read())
    except Exception as e:
        return {'error': str(e)}

# Stop play mode
r = mcp_call('play_stop')
print('Stop:', str(r.get('result', {}).get('structuredContent', r.get('error', 'ok'))))

time.sleep(3)

# Wait for compile if needed
r = mcp_call('editor_status')
text = r.get('result', {}).get('content', [{}])[0].get('text', '')
status = json.loads(text) if text else {}
print('Compiling:', status.get('IsCompiling'))
print('LastCompileSucceeded:', status.get('LastCompileSucceeded'))

# Start play mode
r = mcp_call('play_start')
print('Start:', str(r.get('result', {}).get('structuredContent', r.get('error', 'ok'))))

time.sleep(6)

# Check console for new build message
r = mcp_call('read_console', {'limit': 40})
text = r.get('result', {}).get('content', [{}])[0].get('text', '')
print('\n--- Console ---')
for line in text.split('\n'):
    if any(kw in line.lower() for kw in ['textured', 'visual', 'banner', 'error', 'exception', 'fail', 'neutral market']):
        print(line)

# Now search for new objects
print('\n--- Object Search ---')
for name in ['BannerPole', 'Banner_', 'Goods_', 'Door_', 'GateTorch', 'WellLight']:
    r = mcp_call('find_game_objects', {'name': name})
    text = r.get('result', {}).get('content', [{}])[0].get('text', '')
    data = json.loads(text) if text else {}
    total = data.get('Total', 0)
    print(f'{name}: {total} found')
