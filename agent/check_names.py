import json, urllib.request, re

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=10)
    return json.loads(r.read().decode())

# List all Well objects
r = call('find_game_objects', name='Well')
d = json.loads(r['result']['content'][0]['text'])
print('Well objects (total=' + str(d.get('Total',0)) + '):')
for item in d.get('Results', [])[:10]:
    print('  ' + item['Name'] + '  path=' + item['Path'])
print()

# List stall names
r2 = call('find_game_objects', name='Stall')
d2 = json.loads(r2['result']['content'][0]['text'])
print('Stall objects (total=' + str(d2.get('Total',0)) + '):')
names = set()
for item in d2.get('Results', []):
    # Extract base name (strip trailing numbers)
    base = re.sub(r'_\d+.*$', '', item['Name'])
    names.add(base)
print('  Unique base names:', sorted(names))
print()

# Check TowerMerlon naming
r3 = call('find_game_objects', name='TowerMerlon')
d3 = json.loads(r3['result']['content'][0]['text'])
print('TowerMerlon objects: ' + str(d3.get('Total',0)))
print()

# Check Corner merlons
r4 = call('find_game_objects', name='CornerMerlon')
d4 = json.loads(r4['result']['content'][0]['text'])
print('CornerMerlon objects: ' + str(d4.get('Total',0)))
