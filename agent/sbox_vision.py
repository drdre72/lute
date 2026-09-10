"""
sbox_vision.py — Vision sub-agent using Merlyn's first-person Eyes camera.

Teleports Merlyn (the NPC) to a vantage, points his Eyes camera at the
target, captures a screenshot, sends it to the local Moondream2 vision
model, and returns a text description.

This avoids the "monkey in frame" hallucination problem because the
camera looks outward from Merlyn's eyes — his own body is behind the
camera and not visible.

Usage:
  python sbox_vision.py --look 15748,10236,0 --out south_gate --ask "Describe the gate"
  python sbox_vision.py --orbit 15748,15748,0 --out market --ask "Describe the market"
  python sbox_vision.py --file scrap/eyes_south_gate.png --ask "What do you see?"
"""
import json, urllib.request, base64, os, sys, math, argparse, re, time, io

MCP = 'http://127.0.0.1:7269/mcp'
VISION = 'http://127.0.0.1:1234/v1/chat/completions'
SCRAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'scrap')

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen(MCP, req, timeout=20)
    return json.loads(r.read().decode())

_ids = {}
def find_ids():
    """Cache Merlyn's Eyes camera and LuteBuilderNpc component IDs."""
    global _ids
    if _ids:
        return _ids
    r = call('find_game_objects', name='Merlyn')
    d = json.loads(r['result']['content'][0]['text'])
    merlyn_id = d['Results'][0]['Id']
    go = call('get_game_object', id=merlyn_id)
    t = go['result']['content'][0]['text']
    eyes_match = re.search(r'"Id":"([a-f0-9-]+)","Name":"Eyes"', t)
    npc_match = re.search(r'"Type":"LuteBuilderNpc","Id":"([a-f0-9-]+)"', t)
    eyes_go_id = eyes_match.group(1)
    eyes_go = call('get_game_object', id=eyes_go_id)
    t2 = eyes_go['result']['content'][0]['text']
    cam_match = re.search(r'"Type":"CameraComponent","Id":"([a-f0-9-]+)"', t2)
    _ids = {
        'merlyn': merlyn_id,
        'eyes_go': eyes_go_id,
        'cam_comp': cam_match.group(1),
        'npc_comp': npc_match.group(1),
    }
    return _ids

def teleport_merlyn(pos, target=None, look_angles=None):
    """Teleport Merlyn and point his Eyes camera."""
    ids = find_ids()
    props = {'AgentTeleportTo': pos}
    if target:
        props['AgentTarget'] = target
    if look_angles:
        props['AgentLookAngles'] = look_angles
    else:
        props['AgentLookAngles'] = '0,0,0'  # Clear to use auto-facing
    call('set_component', id=ids['npc_comp'], properties=props)
    time.sleep(1.5)

def capture(width=1280, height=720):
    ids = find_ids()
    r = call('camera_screenshot', camera=ids['cam_comp'],
             width=width, height=height, includeUi=False)
    for item in r['result'].get('content', []):
        if item.get('type') == 'image':
            return base64.b64decode(item['data'])
    return None

def save_img(img_bytes, name):
    os.makedirs(SCRAP, exist_ok=True)
    path = os.path.join(SCRAP, f'{name}.png')
    with open(path, 'wb') as f:
        f.write(img_bytes)
    return path

def ask_vision(img_bytes, question):
    from PIL import Image
    img = Image.open(io.BytesIO(img_bytes)).convert('RGB')
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
                {'type': 'text', 'text': question}
            ]
        }],
        'max_tokens': 400, 'temperature': 0.3
    }
    req = urllib.request.Request(VISION,
        data=json.dumps(payload).encode(),
        headers={'Content-Type': 'application/json'}, method='POST')
    r = urllib.request.urlopen(req, timeout=120)
    result = json.loads(r.read().decode())
    return result['choices'][0]['message']['content']

def vantage(tx, ty, tz, dist, height, yaw):
    """Calculate Merlyn position to look at target from given angle."""
    rad = math.radians(yaw)
    cx = tx + dist * math.sin(rad)
    cy = ty + dist * math.cos(rad)
    cz = tz + height
    return f'{cx:.0f},{cy:.0f},{cz:.0f}', f'{tx:.0f},{ty:.0f},{tz:.0f}'

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='S&Box vision via Merlyn Eyes camera')
    parser.add_argument('--look', help='Target "x,y,z" to look at')
    parser.add_argument('--orbit', help='Orbit point "x,y,z" (8 angles)')
    parser.add_argument('--file', help='Use existing screenshot file')
    parser.add_argument('--out', default='vision', help='Output name prefix')
    parser.add_argument('--ask', default='Describe what you see in this image.', help='Question')
    parser.add_argument('--distance', type=float, default=1000)
    parser.add_argument('--height-offset', type=float, default=200)
    args = parser.parse_args()

    if args.file:
        img_path = args.file if os.path.isabs(args.file) else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', args.file)
        if not os.path.exists(img_path):
            print(f'File not found: {img_path}')
            sys.exit(1)
        with open(img_path, 'rb') as f:
            img_bytes = f.read()
        print(f'Asking vision about {img_path}...')
        answer = ask_vision(img_bytes, args.ask)
        print(f'\n--- VISION ---\n{answer}\n')
    elif args.orbit:
        tx, ty, tz = [float(x) for x in args.orbit.split(',')]
        print(f'Orbiting ({tx},{ty},{tz}) — 8 angles')
        for i in range(8):
            yaw = i * 45
            pos, target = vantage(tx, ty, tz, args.distance, args.height_offset, yaw)
            teleport_merlyn(pos, target)
            img = capture()
            if img:
                path = save_img(img, f'{args.out}_orbit_{yaw:03d}')
                print(f'\n=== Angle {yaw} ===')
                answer = ask_vision(img, args.ask)
                print(answer)
    elif args.look:
        tx, ty, tz = [float(x) for x in args.look.split(',')]
        pos, target = vantage(tx, ty, tz, args.distance, args.height_offset, 180)
        teleport_merlyn(pos, target)
        img = capture()
        if img:
            path = save_img(img, args.out)
            print(f'Captured {len(img)} bytes -> {path}')
            answer = ask_vision(img, args.ask)
            print(f'\n--- VISION ---\n{answer}\n')
    else:
        print('Use --look, --orbit, or --file')
        sys.exit(1)
