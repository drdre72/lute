"""Reusable wall camera helper — positions the editor camera at the
optimal viewing position for the active wall being built.

The bricks are laid at the same origin (wall at x=-21252, y=-16968),
so this camera position is stable across sessions.

Usage:
    python _wall_camera.py          # set camera and screenshot
    python _wall_camera.py --no-shot # just set camera, no screenshot
"""
import requests, json, sys, base64, math
sys.stdout.reconfigure(encoding='utf-8')

MCP = 'http://127.0.0.1:7269/mcp'

def call(name, args=None):
    args = args or {}
    r = requests.post(MCP, json={'jsonrpc':'2.0','id':1,'method':'tools/call','params':{'name':name,'arguments':args}}, timeout=20)
    return r.json()

# ── Fixed wall camera position ──
# Wall origin: x=-21252, y=-16968 (Wall_W_54 area)
# Camera: elevated, looking down at the wall face
# This is the "2nd Camera" equivalent — stable across sessions.

CAM_X = -21141
CAM_Y = -16968
CAM_Z = 120
PITCH = 39.0
YAW = 180.0

def set_wall_camera():
    """Position the editor camera at the optimal wall viewing position."""
    pos = f"{CAM_X},{CAM_Y},{CAM_Z}"
    angles = f"{PITCH},{YAW},0"
    r = call('set_editor_camera', {'position': pos, 'angles': angles, 'fieldOfView': 60})
    print(f"Camera set to ({CAM_X},{CAM_Y},{CAM_Z}) pitch={PITCH} yaw={YAW}")
    return r

def screenshot(path='scrap/wall_camera.png'):
    """Capture a screenshot from the current editor camera position."""
    r = call('editor_camera_screenshot', {'width': 1280, 'height': 720})
    content = r.get('result', {}).get('content', [])
    for c in content:
        if c.get('type') == 'image':
            img_data = base64.b64decode(c['data'])
            with open(path, 'wb') as f:
                f.write(img_data)
            print(f"Saved {path} ({len(img_data)} bytes)")
            return path
    print("No image in response")
    return None

if __name__ == '__main__':
    set_wall_camera()
    if '--no-shot' not in sys.argv:
        screenshot()
