"""Capture from Merlyn's first-person Eyes camera."""
import json, urllib.request, re, base64, time

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=15)
    return json.loads(r.read().decode())

# Find Merlyn
r = call('find_game_objects', name='Merlyn')
d = json.loads(r['result']['content'][0]['text'])
merlyn_id = d['Results'][0]['Id']

# Get Eyes child ID
go = call('get_game_object', id=merlyn_id)
t = go['result']['content'][0]['text']
eyes_match = re.search(r'"Id":"([a-f0-9-]+)","Name":"Eyes"', t)
eyes_go_id = eyes_match.group(1)
print('Eyes GameObject:', eyes_go_id)

# Get CameraComponent from Eyes
eyes_go = call('get_game_object', id=eyes_go_id)
t2 = eyes_go['result']['content'][0]['text']
cam_match = re.search(r'"Type":"CameraComponent","Id":"([a-f0-9-]+)"', t2)
cam_comp_id = cam_match.group(1)
print('CameraComponent:', cam_comp_id)

# Find LuteBuilderNpc component
npc_match = re.search(r'"Type":"LuteBuilderNpc","Id":"([a-f0-9-]+)"', t)
npc_comp_id = npc_match.group(1)
print('LuteBuilderNpc:', npc_comp_id)

# Teleport Merlyn to the south gate, looking north into the market
# South gate at (15748, 10236, 0). Merlyn at (15748, 9800, 0) looking north.
call('set_component', id=npc_comp_id, properties={'AgentTeleportTo': '15748,9800,100', 'AgentTarget': '15748,15748,100', 'AgentLookAngles': '-10,0,0'})
time.sleep(2)

# Capture from Eyes camera
r2 = call('camera_screenshot', camera=cam_comp_id, width=1280, height=720, includeUi=False)
for item in r2['result'].get('content', []):
    if item.get('type') == 'image':
        img = base64.b64decode(item['data'])
        outpath = 'C:/Users/Shadow/Documents/lute/scrap/eyes_south_gate.png'
        with open(outpath, 'wb') as f:
            f.write(img)
        print(f'Captured {len(img)} bytes -> {outpath}')

        # Analyze
        from PIL import Image
        import collections
        im = Image.open(outpath)
        pixels = list(im.getdata())
        counts = collections.Counter(pixels)
        print(f'Unique colors: {len(counts)}')
        print(f'Top: {counts.most_common(3)}')
