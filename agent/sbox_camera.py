"""
sbox_camera.py — Player-based camera observation tool for the Lute project.

Teleports the PLAYER to a vantage point (the camera follows the player)
and captures a screenshot. This works because the PlayerController
overrides the Main Camera position every frame, so moving the camera
GameObject directly has no effect — we must move the player.

Usage:
  python sbox_camera.py --look 15748,10236,0 --out south_gate
  python sbox_camera.py --orbit 15748,15748,0 --out market
  python sbox_camera.py --pos "15748,9500,100" --angles "-10,0,0" --out custom
"""
import json, urllib.request, base64, os, sys, math, argparse, re, time

MCP = 'http://127.0.0.1:7269/mcp'
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
    return None

def move_player(pos, angles=None):
    args = {'id': find_player(), 'position': pos}
    if angles:
        args['angles'] = angles
    call('set_game_object', **args)

def screenshot(width=1280, height=720):
    r = call('camera_screenshot', width=width, height=height, includeUi=False)
    for item in r['result'].get('content', []):
        if item.get('type') == 'image':
            return base64.b64decode(item['data'])
    return None

def save_screenshot(img_bytes, name):
    os.makedirs(SCRAP, exist_ok=True)
    path = os.path.join(SCRAP, f'{name}.png')
    with open(path, 'wb') as f:
        f.write(img_bytes)
    return path

def vantage(target_x, target_y, target_z, distance=1500, height=400, yaw=0):
    """Calculate player position and angles to look at a target."""
    rad = math.radians(yaw)
    cam_x = target_x + distance * math.sin(rad)
    cam_y = target_y + distance * math.cos(rad)
    cam_z = target_z + height
    # Pitch: look down at target
    dx = target_x - cam_x
    dy = target_y - cam_y
    dz = target_z - cam_z
    pitch = math.degrees(math.atan2(-dz, math.sqrt(dx*dx + dy*dy)))
    # Yaw: direction from camera to target
    yaw_deg = math.degrees(math.atan2(-dx, -dy))
    return f'{cam_x:.0f},{cam_y:.0f},{cam_z:.0f}', f'{pitch:.1f},{yaw_deg:.1f},0'

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='S&Box camera observation via player teleport')
    parser.add_argument('--look', help='Target to look at "x,y,z"')
    parser.add_argument('--orbit', help='Orbit around point "x,y,z" (8 angles)')
    parser.add_argument('--pos', help='Direct player position "x,y,z"')
    parser.add_argument('--angles', help='Player angles "pitch,yaw,roll"')
    parser.add_argument('--out', default='capture', help='Output filename prefix')
    parser.add_argument('--width', type=int, default=1280)
    parser.add_argument('--height', type=int, default=720)
    parser.add_argument('--distance', type=float, default=1500)
    parser.add_argument('--height-offset', type=float, default=400)
    args = parser.parse_args()

    pid = find_player()
    if not pid:
        print('ERROR: Player Controller not found')
        sys.exit(1)
    print(f'Player: {pid}')

    if args.orbit:
        tx, ty, tz = [float(x) for x in args.orbit.split(',')]
        print(f'Orbiting ({tx},{ty},{tz}) — 8 angles')
        for i in range(8):
            yaw = i * 45
            pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset, yaw)
            move_player(pos, angles)
            time.sleep(1)
            img = screenshot(args.width, args.height)
            if img:
                path = save_screenshot(img, f'{args.out}_orbit_{yaw:03d}')
                print(f'  {yaw:3d}°: {len(img)} bytes -> {path}')
    elif args.look:
        tx, ty, tz = [float(x) for x in args.look.split(',')]
        pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset)
        move_player(pos, angles)
        time.sleep(1)
        img = screenshot(args.width, args.height)
        if img:
            path = save_screenshot(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes) looking at ({tx},{ty},{tz})')
    elif args.pos:
        move_player(args.pos, args.angles)
        time.sleep(1)
        img = screenshot(args.width, args.height)
        if img:
            path = save_screenshot(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes) at {args.pos}')
    else:
        img = screenshot(args.width, args.height)
        if img:
            path = save_screenshot(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes)')
