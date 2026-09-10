"""
Systematic vision inspection of the Neutral Market.
Teleports player to each feature, captures screenshot, asks Moondream
to describe it. Compares vision output against known geometry.
"""
import json, urllib.request, base64, os, time, math, re

MCP = 'http://127.0.0.1:7269/mcp'
VISION = 'http://127.0.0.1:1234/v1/chat/completions'
SCRAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'scrap')

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen(MCP, req, timeout=20)
    return json.loads(r.read().decode())

_player_id = None
def player():
    global _player_id
    if _player_id:
        return _player_id
    r = call('find_game_objects', name='Player Controller')
    d = json.loads(r['result']['content'][0]['text'])
    _player_id = d['Results'][0]['Id']
    return _player_id

def teleport(pos, angles):
    call('set_game_object', id=player(), position=pos, angles=angles)
    time.sleep(1.5)

def capture():
    r = call('camera_screenshot', width=1280, height=720, includeUi=False)
    for item in r['result'].get('content', []):
        if item.get('type') == 'image':
            return base64.b64decode(item['data'])
    return None

def ask_vision(img_bytes, question):
    from PIL import Image
    import io
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
    rad = math.radians(yaw)
    cx = tx + dist * math.sin(rad)
    cy = ty + dist * math.cos(rad)
    cz = tz + height
    dx, dy, dz = tx - cx, ty - cy, tz - cz
    pitch = math.degrees(math.atan2(-dz, math.sqrt(dx*dx + dy*dy)))
    yaw_deg = math.degrees(math.atan2(-dx, -dy))
    return f'{cx:.0f},{cy:.0f},{cz:.0f}', f'{pitch:.1f},{yaw_deg:.1f},0'

# === INSPECTION ===
print("=== NEUTRAL MARKET VISION INSPECTION ===\n")

# Monument center
CX, CY = 15748, 15748

shots = [
    # (name, target, distance, height, yaw, question)
    ("south_gate",       (CX, 10236, 100), 800, 300, 180,
     "This is a video game scene showing a gate in a wall. Describe the gate, the wall, and any towers. Does the gate look centered and symmetrical?"),
    ("north_gate",       (CX, 21260, 100), 800, 300, 0,
     "This is a video game scene showing a gate in a wall. Describe the gate, the wall, and any towers. Does the gate look centered and symmetrical?"),
    ("east_gate",        (21260, CY, 100), 800, 300, 270,
     "This is a video game scene showing a gate in a wall. Describe the gate, the wall, and any towers. Does the gate look centered and symmetrical?"),
    ("plaza_from_side",  (CX, CY, 100), 600, 400, 90,
     "This is a video game scene showing a market plaza. Describe what structures you see - stalls, well, buildings. What materials do they appear to be made of?"),
    ("tower_close",      (15354, 10236, 400), 500, 200, 180,
     "This is a video game scene showing a tower. Describe the tower - its shape, height, and any details on top. Are there battlements or crenellations?"),
    ("wall_battlements", (CX, 21260, 500), 600, 100, 0,
     "This is a video game scene showing the top of a wall. Are there battlements (merlons - solid blocks with gaps between them) visible on top of the wall?"),
    ("moat_area",        (CX, 9646, 0), 600, 200, 180,
     "This is a video game scene showing the area outside a walled market. Is there a moat, ditch, or water feature visible? What does the ground look like?"),
    ("market_overview",  (CX, CY, 100), 3000, 1500, 45,
     "This is a top-down view of a fortified market in a video game. Describe the overall layout - walls, gates, towers, and interior structures. Is it roughly square?"),
]

for name, (tx, ty, tz), dist, height, yaw, question in shots:
    print(f"--- {name} ---")
    pos, angles = vantage(tx, ty, tz, dist, height, yaw)
    teleport(pos, angles)
    img = capture()
    if not img:
        print("  CAPTURE FAILED")
        continue
    # Save
    os.makedirs(SCRAP, exist_ok=True)
    path = os.path.join(SCRAP, f'vision_{name}.png')
    with open(path, 'wb') as f:
        f.write(img)
    print(f"  Captured {len(img)} bytes -> {path}")
    # Ask vision
    answer = ask_vision(img, question)
    print(f"  Q: {question[:80]}...")
    print(f"  A: {answer}")
    print()
