"""
sbox_vision.py — Vision sub-agent for the Lute project.

Captures a screenshot via the S&Box MCP camera tool, sends it to the
local Moondream2 vision model (OpenAI-compatible API at localhost:1234),
and returns a text description. This is the "eyes" that raycasts can't
replace — it answers "does this look right" instead of "is there
geometry here."

Usage:
  python sbox_vision.py --look 15748,10236,0 --out south_gate --ask "Describe this scene"
  python sbox_vision.py --file scrap/south_gate.png --ask "Is the archway centered?"
  python sbox_vision.py --orbit 15748,15748,0 --out market --ask "Describe the market"
"""
import json, urllib.request, base64, os, sys, math, argparse, re, time

MCP = 'http://127.0.0.1:7269/mcp'
VISION_API = 'http://127.0.0.1:1234/v1/chat/completions'
SCRAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'scrap')

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen(MCP, req, timeout=20)
    return json.loads(r.read().decode())

_player_id = None
def find_player():
    global _player_id
    if _player_id:
        return _player_id
    r = call('find_game_objects', name='Player Controller')
    d = json.loads(r['result']['content'][0]['text'])
    if d.get('Results'):
        _player_id = d['Results'][0]['Id']
    return _player_id

def move_player(pos, angles=None):
    args = {'id': find_player(), 'position': pos}
    if angles:
        args['angles'] = angles
    call('set_game_object', **args)

def capture(width=1280, height=720):
    r = call('camera_screenshot', width=width, height=height, includeUi=False)
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

def ask_vision(image_path, question):
    """Send image + question to Moondream2, get text back."""
    with open(image_path, 'rb') as f:
        img_b64 = base64.b64encode(f.read()).decode()

    payload = {
        'model': 'moondream-2b-2025-04-14',
        'messages': [
            {
                'role': 'user',
                'content': [
                    {'type': 'text', 'text': question},
                    {'type': 'image_url', 'image_url': {'url': f'data:image/png;base64,{img_b64}'}}
                ]
            }
        ],
        'max_tokens': 500,
        'temperature': 0.3
    }

    req = urllib.request.Request(
        VISION_API,
        data=json.dumps(payload).encode(),
        headers={'Content-Type': 'application/json'},
        method='POST'
    )
    r = urllib.request.urlopen(req, timeout=60)
    result = json.loads(r.read().decode())
    return result['choices'][0]['message']['content']

def vantage(target_x, target_y, target_z, distance=1500, height=400, yaw=0):
    rad = math.radians(yaw)
    cam_x = target_x + distance * math.sin(rad)
    cam_y = target_y + distance * math.cos(rad)
    cam_z = target_z + height
    dx = target_x - cam_x
    dy = target_y - cam_y
    dz = target_z - cam_z
    pitch = math.degrees(math.atan2(-dz, math.sqrt(dx*dx + dy*dy)))
    yaw_deg = math.degrees(math.atan2(-dx, -dy))
    return f'{cam_x:.0f},{cam_y:.0f},{cam_z:.0f}', f'{pitch:.1f},{yaw_deg:.1f},0'

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='S&Box vision via Moondream2')
    parser.add_argument('--look', help='Target "x,y,z" to look at')
    parser.add_argument('--orbit', help='Orbit point "x,y,z" (8 angles)')
    parser.add_argument('--file', help='Use existing screenshot file')
    parser.add_argument('--out', default='vision', help='Output name prefix')
    parser.add_argument('--ask', default='Describe what you see in this scene in detail.', help='Question for the vision model')
    parser.add_argument('--distance', type=float, default=1500)
    parser.add_argument('--height-offset', type=float, default=400)
    args = parser.parse_args()

    if args.file:
        # Use existing file
        img_path = args.file if os.path.isabs(args.file) else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', args.file)
        if not os.path.exists(img_path):
            print(f'File not found: {img_path}')
            sys.exit(1)
        print(f'Asking vision about {img_path}...')
        answer = ask_vision(img_path, args.ask)
        print(f'\n--- VISION RESPONSE ---\n{answer}\n')
    elif args.orbit:
        tx, ty, tz = [float(x) for x in args.orbit.split(',')]
        print(f'Orbiting ({tx},{ty},{tz}) — 8 angles')
        for i in range(8):
            yaw = i * 45
            pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset, yaw)
            move_player(pos, angles)
            time.sleep(1)
            img = capture()
            if img:
                img_path = save_img(img, f'{args.out}_orbit_{yaw:03d}')
                print(f'\n=== Angle {yaw} ===')
                answer = ask_vision(img_path, args.ask)
                print(answer)
    elif args.look:
        tx, ty, tz = [float(x) for x in args.look.split(',')]
        pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset)
        move_player(pos, angles)
        time.sleep(1)
        img = capture()
        if img:
            img_path = save_img(img, args.out)
            print(f'Captured {img_path} ({len(img)} bytes)')
            answer = ask_vision(img_path, args.ask)
            print(f'\n--- VISION RESPONSE ---\n{answer}\n')
    else:
        print('Use --look, --orbit, or --file')
        sys.exit(1)
