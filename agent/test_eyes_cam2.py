"""Capture from Merlyn's Eyes camera with better angle."""
import json, urllib.request, re, base64, time

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=15)
    return json.loads(r.read().decode())

# Find Merlyn and components
r = call('find_game_objects', name='Merlyn')
d = json.loads(r['result']['content'][0]['text'])
merlyn_id = d['Results'][0]['Id']
go = call('get_game_object', id=merlyn_id)
t = go['result']['content'][0]['text']

# Find Eyes and NPC component IDs
eyes_match = re.search(r'"Id":"([a-f0-9-]+)","Name":"Eyes"', t)
eyes_go_id = eyes_match.group(1)
eyes_go = call('get_game_object', id=eyes_go_id)
t2 = eyes_go['result']['content'][0]['text']
cam_match = re.search(r'"Type":"CameraComponent","Id":"([a-f0-9-]+)"', t2)
cam_comp_id = cam_match.group(1)
npc_match = re.search(r'"Type":"LuteBuilderNpc","Id":"([a-f0-9-]+)"', t)
npc_comp_id = npc_match.group(1)

print(f'Eyes: {eyes_go_id}, Camera: {cam_comp_id}, NPC: {npc_comp_id}')

# Teleport Merlyn to the exact position that worked before
call('set_component', id=npc_comp_id, properties={
    'AgentTeleportTo': '15748,9800,100',
    'AgentTarget': '15748,15748,100',
    'AgentLookAngles': '0,0,0'
})
time.sleep(2)

# Verify positions
go2 = call('get_game_object', id=merlyn_id)
t3 = go2['result']['content'][0]['text']
pos = re.search(r'WorldPosition...([0-9.,-]+)', t3)
print(f'Merlyn pos: {pos.group(1)}')

eyes_go2 = call('get_game_object', id=eyes_go_id)
t4 = eyes_go2['result']['content'][0]['text']
epos = re.search(r'WorldPosition...([0-9.,-]+)', t4)
erot = re.search(r'WorldRotation...([0-9.,-]+)', t4)
print(f'Eyes pos: {epos.group(1)}')
print(f'Eyes rot: {erot.group(1)}')

# Capture
r2 = call('camera_screenshot', camera=cam_comp_id, width=1280, height=720, includeUi=False)
for item in r2['result'].get('content', []):
    if item.get('type') == 'image':
        img = base64.b64decode(item['data'])
        outpath = 'C:/Users/Shadow/Documents/lute/scrap/eyes_south_gate2.png'
        with open(outpath, 'wb') as f:
            f.write(img)
        print(f'Captured {len(img)} bytes')

        from PIL import Image
        import collections
        im = Image.open(outpath)
        pixels = list(im.getdata())
        counts = collections.Counter(pixels)
        print(f'Unique: {len(counts)}, top: {counts.most_common(3)}')

# Ask Moondream
import io
img = Image.open(outpath).convert('RGB')
img = img.resize((640, 360))
buf = io.BytesIO()
img.save(buf, format='JPEG', quality=90)
img_b64 = base64.b64encode(buf.getvalue()).decode()
payload = {
    'model': 'moondream-2b-2025-04-14',
    'messages': [{
        'role': 'user',
        'content': [
            {'type': 'image_url', 'image_url': {'url': f'data:image/jpeg;base64,{img_b64}'}},
            {'type': 'text', 'text': 'This is a first-person view in a video game. What structures do you see? Is there a wall or gate?'}
        ]
    }],
    'max_tokens': 400, 'temperature': 0.3
}
req = urllib.request.Request('http://127.0.0.1:1234/v1/chat/completions',
    data=json.dumps(payload).encode(),
    headers={'Content-Type': 'application/json'}, method='POST')
r3 = urllib.request.urlopen(req, timeout=120)
result = json.loads(r3.read().decode())
print(f'Vision: {result["choices"][0]["message"]["content"]}')
