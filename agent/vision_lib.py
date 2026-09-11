"""
vision_lib.py — Shared library for S&Box camera control, screenshot capture,
and Qwen3-VL vision queries (via LM Studio's OpenAI-compatible endpoint).
Used by sbox_vision.py, sbox_camera.py, drive_merlyn.py, texture_inspect.py.

Two capture modes:
  - merlyn: teleport Merlyn NPC, capture from his Eyes camera (over-the-shoulder)
  - player: teleport the Player Controller, capture from the main camera

Usage from other scripts:
  from vision_lib import VisionLib
  vl = VisionLib()
  vl.teleport_merlyn(pos, target=target)
  img = vl.capture_merlyn()
  answer = vl.ask_vision(img, "Describe what you see")
"""
import json, urllib.request, base64, os, sys, math, re, time, io

MCP = 'http://127.0.0.1:7269/mcp'
VISION = 'http://127.0.0.1:1234/v1/chat/completions'
MODEL = 'qwen3-vl-4b-instruct'
SCRAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'scrap')


def call(tool, **args):
    """Make a single MCP JSON-RPC call."""
    req = json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'tools/call',
                      'params': {'name': tool, 'arguments': args}}).encode()
    r = urllib.request.urlopen(MCP, req, timeout=20)
    return json.loads(r.read().decode())


def vantage(tx, ty, tz, distance=1500, height=400, yaw=0):
    """Calculate observer position and look angles to view a target point.

    Args:
        tx, ty, tz: Target world position to look at.
        distance: How far from the target (world units).
        height: How high above the target (world units).
        yaw: Orbit angle in degrees (0 = north, 90 = east, etc.).

    Returns:
        (position_str, angles_str) — "x,y,z" and "pitch,yaw,roll"
    """
    rad = math.radians(yaw)
    cx = tx + distance * math.sin(rad)
    cy = ty + distance * math.cos(rad)
    cz = tz + height
    dx, dy, dz = tx - cx, ty - cy, tz - cz
    pitch = math.degrees(math.atan2(-dz, math.sqrt(dx * dx + dy * dy)))
    yaw_deg = math.degrees(math.atan2(-dx, -dy))
    return f'{cx:.0f},{cy:.0f},{cz:.0f}', f'{pitch:.1f},{yaw_deg:.1f},0'


def save_img(img_bytes, name):
    """Save screenshot bytes to scrap/<name>.png. Returns the path."""
    os.makedirs(SCRAP, exist_ok=True)
    path = os.path.join(SCRAP, f'{name}.png')
    with open(path, 'wb') as f:
        f.write(img_bytes)
    return path


def ask_vision(img_bytes, question, model=MODEL):
    """Send an image + question to the local Qwen3-VL vision model
    (via LM Studio's OpenAI-compatible /v1/chat/completions endpoint).

    Converts to JPEG, resizes to 640x360, base64-encodes, and POSTs.

    Returns the text response, or empty string on failure.
    """
    from PIL import Image
    img = Image.open(io.BytesIO(img_bytes)).convert('RGB')
    img = img.resize((640, 360))
    buf = io.BytesIO()
    img.save(buf, format='JPEG', quality=90)
    img_b64 = base64.b64encode(buf.getvalue()).decode()
    payload = {
        'model': model,
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
    try:
        r = urllib.request.urlopen(req, timeout=120)
        result = json.loads(r.read().decode())
        return result['choices'][0]['message']['content']
    except Exception as e:
        return f'[VISION ERROR: {e}]'


def ask_grounding(img_bytes, question, model=MODEL):
    """Ask Qwen3-VL to locate an object and return a bounding box.

    Uses Qwen3-VL's native <box> tag format: the model returns
    <box>(x1,y1),(x2,y2)</box> with coordinates normalized to 0-1000.

    Returns a dict with:
      - found: bool
      - box: (x1, y1, x2, y2) in 640x360 pixel coords, or None
      - raw: the full text response
    """
    from PIL import Image
    import re

    img = Image.open(io.BytesIO(img_bytes)).convert('RGB')
    img = img.resize((640, 360))
    buf = io.BytesIO()
    img.save(buf, format='JPEG', quality=90)
    img_b64 = base64.b64encode(buf.getvalue()).decode()

    # Ask for <box> format specifically, plus a fallback JSON
    full_prompt = (
        f"{question}\n\n"
        "Output the bounding box using <box>(x1,y1),(x2,y2)</box> format "
        "where coordinates are normalized to 0-1000. "
        "If the object is not visible, say 'not found'."
    )
    payload = {
        'model': model,
        'messages': [{
            'role': 'user',
            'content': [
                {'type': 'image_url', 'image_url': {'url': f'data:image/jpeg;base64,{img_b64}'}},
                {'type': 'text', 'text': full_prompt}
            ]
        }],
        'max_tokens': 300, 'temperature': 0.1
    }
    req = urllib.request.Request(VISION,
        data=json.dumps(payload).encode(),
        headers={'Content-Type': 'application/json'}, method='POST')
    try:
        r = urllib.request.urlopen(req, timeout=120)
        result = json.loads(r.read().decode())
        raw = result['choices'][0]['message']['content']
    except Exception as e:
        return {'found': False, 'box': None, 'raw': f'[VISION ERROR: {e}]'}

    # Parse <box>(x1,y1),(x2,y2)</box> — normalized 0-1000
    boxes = re.findall(r'<box>\s*\((\d+),(\d+)\),\s*\((\d+),(\d+)\)\s*</box>', raw)
    if boxes:
        x1, y1, x2, y2 = [int(v) for v in boxes[0]]
        # Convert from 0-1000 normalized to 640x360 pixel coords
        px1 = int(x1 * 640 / 1000)
        py1 = int(y1 * 360 / 1000)
        px2 = int(x2 * 640 / 1000)
        py2 = int(y2 * 360 / 1000)
        return {'found': True, 'box': (px1, py1, px2, py2),
                'box_norm': (x1, y1, x2, y2), 'raw': raw}

    # Fallback: try JSON format {"x":, "y":, "width":, "height":}
    try:
        # Extract JSON from the response
        json_match = re.search(r'\{[^}]+\}', raw)
        if json_match:
            j = json.loads(json_match.group())
            if 'found' in j and j['found'] is False:
                return {'found': False, 'box': None, 'raw': raw}
            if 'x' in j and 'y' in j:
                x, y = int(j['x']), int(j['y'])
                w = int(j.get('width', 50))
                h = int(j.get('height', 50))
                return {'found': True, 'box': (x, y, x + w, y + h), 'raw': raw}
    except (json.JSONDecodeError, ValueError, KeyError):
        pass

    if 'not found' in raw.lower():
        return {'found': False, 'box': None, 'raw': raw}

    return {'found': False, 'box': None, 'raw': raw}


def project_world_to_screen(world_pos, cam_pos, cam_angles, img_w=640, img_h=360):
    """Project a 3D world position to approximate screen pixel coordinates.

    This is a rough projection — not a full view matrix — but sufficient
    for cross-checking Qwen's bounding box against where an object should
    appear given Merlyn's known camera position and angles.

    Args:
        world_pos: (x, y, z) world position of the target object
        cam_pos: (x, y, z) camera world position
        cam_angles: (pitch, yaw, roll) camera angles in degrees
        img_w, img_h: image dimensions

    Returns:
        (px, py) approximate screen pixel, or None if behind camera
    """
    import math
    dx = world_pos[0] - cam_pos[0]
    dy = world_pos[1] - cam_pos[1]
    dz = world_pos[2] - cam_pos[2]

    # Camera looks along -Y by default (yaw=0 = looking south/north)
    # Apply yaw rotation
    yaw_rad = math.radians(cam_angles[1])
    # Rotate world delta into camera space
    cx = dx * math.cos(yaw_rad) - dy * math.sin(yaw_rad)
    cy = dx * math.sin(yaw_rad) + dy * math.cos(yaw_rad)
    cz = dz

    # Apply pitch rotation
    pitch_rad = math.radians(cam_angles[0])
    cy_pitched = cy * math.cos(pitch_rad) + cz * math.sin(pitch_rad)
    cz_pitched = -cy * math.sin(pitch_rad) + cz * math.cos(pitch_rad)

    # In camera space, camera looks along -Y (forward)
    # If cy_pitched > 0, object is behind camera
    if cy_pitched >= 0:
        return None

    # Perspective projection
    fov = 60  # approximate FOV in degrees
    f = 1.0 / math.tan(math.radians(fov / 2))
    dist = -cy_pitched  # distance forward (positive)
    if dist < 1:
        return None

    sx = (cx * f / dist)  # -1 to 1
    sy = (cz_pitched * f / dist)  # -1 to 1

    # Map to pixel coordinates
    px = int((sx + 1) * 0.5 * img_w)
    py = int((1 - (sy + 1) * 0.5) * img_h)  # flip Y (screen Y goes down)

    return (px, py)


def _safe_print(text):
    """Print text safely on Windows (replace non-CP1252 chars)."""
    try:
        print(text)
    except UnicodeEncodeError:
        print(text.encode('ascii', 'replace').decode('ascii'))


def pixel_check(img_bytes):
    """Quick sanity check on a screenshot. Returns (ok, stats_dict).

    Catches 'just sky' or 'capture failed' without burning a VLM call.
    """
    from PIL import Image
    import collections
    img = Image.open(io.BytesIO(img_bytes))
    pixels = list(img.getdata())
    total = len(pixels)
    bright = sum(1 for p in pixels if sum(p[:3]) / 3 > 200)
    mid = sum(1 for p in pixels if 100 < sum(p[:3]) / 3 <= 200)
    dark = sum(1 for p in pixels if sum(p[:3]) / 3 <= 100)
    counts = collections.Counter(pixels)
    top3 = counts.most_common(3)
    # If one color dominates >80%, likely a failed capture
    dominant_pct = top3[0][1] / total if top3 else 1.0
    ok = dominant_pct < 0.80
    return ok, {
        'total': total,
        'bright_pct': bright / total,
        'mid_pct': mid / total,
        'dark_pct': dark / total,
        'dominant_pct': dominant_pct,
        'top3': [(c[:3], cnt / total) for c, cnt in top3],
    }


# ── Screenshot diffing against a known-good baseline ──
# Baselines are stored in scrap/baselines/<name>.png. On the first run
# (no baseline exists), the capture becomes the baseline and the diff
# is skipped. On subsequent runs, MSE is computed between the current
# capture and the baseline. If MSE is below a threshold, the frame is
# considered "unchanged" and the VLM call can be skipped entirely.
# If MSE is above a higher threshold, something changed and the VLM
# should inspect. Between the two thresholds is "marginal" — fall
# through to the VLM but flag it.

BASELINE_DIR = os.path.join(SCRAP, 'baselines')
DIFF_SAME = 50.0       # MSE below this = essentially identical, skip VLM
DIFF_CHANGED = 200.0   # MSE above this = clearly changed, VLM should inspect


def img_diff(img_bytes, baseline_name):
    """Compare a screenshot against a saved baseline.

    Returns (mse, status, baseline_path) where:
      - mse: mean squared error (0.0 = identical, higher = more different)
      - status: 'same' (skip VLM), 'changed' (VLM should inspect),
                'marginal' (VLM but flag), 'no_baseline' (first run,
                baseline saved)
      - baseline_path: path to the baseline file (or None)
    """
    from PIL import Image
    import numpy as np

    os.makedirs(BASELINE_DIR, exist_ok=True)
    baseline_path = os.path.join(BASELINE_DIR, f'{baseline_name}.png')

    img = Image.open(io.BytesIO(img_bytes)).convert('RGB')
    img_resized = img.resize((320, 180))  # small for fast diff

    if not os.path.exists(baseline_path):
        # First run — save as baseline
        img_resized.save(baseline_path)
        return 0.0, 'no_baseline', baseline_path

    baseline = Image.open(baseline_path).convert('RGB').resize((320, 180))
    arr_cur = np.asarray(img_resized, dtype=np.float32)
    arr_base = np.asarray(baseline, dtype=np.float32)
    mse = float(np.mean((arr_cur - arr_base) ** 2))

    if mse < DIFF_SAME:
        status = 'same'
    elif mse > DIFF_CHANGED:
        status = 'changed'
    else:
        status = 'marginal'

    return mse, status, baseline_path


def update_baseline(img_bytes, baseline_name):
    """Save or overwrite the baseline for a viewpoint.
    Call this after a VLM verdict of 'appropriate' or 'ok' to lock in
    a known-good capture for future diffing."""
    from PIL import Image
    os.makedirs(BASELINE_DIR, exist_ok=True)
    baseline_path = os.path.join(BASELINE_DIR, f'{baseline_name}.png')
    img = Image.open(io.BytesIO(img_bytes)).convert('RGB')
    img.resize((320, 180)).save(baseline_path)
    return baseline_path


def overlay_grid(img_bytes, spacing=100, color=(128, 128, 128, 80)):
    """Burn a light pixel-space grid onto a screenshot before sending
    to the VLM. VLMs are known to make better spatial judgments
    ("is this centered," "is this offset left") with gridlines to
    reference rather than judging raw pixel position.

    Args:
        img_bytes: original screenshot bytes
        spacing: grid line spacing in pixels (in the 640x360 resized image)
        color: (R, G, B, A) — light gray, semi-transparent by default

    Returns: new image bytes with grid overlay
    """
    from PIL import Image, ImageDraw
    img = Image.open(io.BytesIO(img_bytes)).convert('RGBA')
    # Resize to the size Qwen will see
    img = img.resize((640, 360))
    overlay = Image.new('RGBA', (640, 360), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    # Vertical lines
    for x in range(0, 640, spacing):
        draw.line([(x, 0), (x, 360)], fill=color, width=1)
    # Horizontal lines
    for y in range(0, 360, spacing):
        draw.line([(0, y), (640, y)], fill=color, width=1)
    # Composite grid over image
    result = Image.alpha_composite(img, overlay).convert('RGB')
    buf = io.BytesIO()
    result.save(buf, format='PNG')
    return buf.getvalue()


class VisionLib:
    """Stateful wrapper that caches object lookups (Merlyn, Eyes, Player)."""

    def __init__(self):
        self._merlyn_id = None
        self._eyes_go_id = None
        self._cam_comp_id = None
        self._npc_comp_id = None
        self._player_id = None

    # --- Lookups (by name, never hardcoded GUIDs) ---

    def _find_merlyn(self):
        """Look up Merlyn NPC + Eyes camera + LuteBuilderNpc component."""
        if self._merlyn_id:
            return
        r = call('find_game_objects', name='Merlyn')
        d = json.loads(r['result']['content'][0]['text'])
        if not d.get('Results'):
            raise RuntimeError('Merlyn not found — is play mode running?')
        self._merlyn_id = d['Results'][0]['Id']
        go = call('get_game_object', id=self._merlyn_id)
        t = go['result']['content'][0]['text']
        eyes_match = re.search(r'"Id":"([a-f0-9-]+)","Name":"Eyes"', t)
        npc_match = re.search(r'"Type":"LuteBuilderNpc","Id":"([a-f0-9-]+)"', t)
        if not eyes_match or not npc_match:
            raise RuntimeError('Merlyn missing Eyes camera or LuteBuilderNpc component')
        self._eyes_go_id = eyes_match.group(1)
        self._npc_comp_id = npc_match.group(1)
        eyes_go = call('get_game_object', id=self._eyes_go_id)
        t2 = eyes_go['result']['content'][0]['text']
        cam_match = re.search(r'"Type":"CameraComponent","Id":"([a-f0-9-]+)"', t2)
        if not cam_match:
            raise RuntimeError('Eyes GameObject missing CameraComponent')
        self._cam_comp_id = cam_match.group(1)

    def _find_player(self):
        """Look up the Player Controller by name."""
        if self._player_id:
            return
        r = call('find_game_objects', name='Player Controller')
        d = json.loads(r['result']['content'][0]['text'])
        if not d.get('Results'):
            raise RuntimeError('Player Controller not found')
        self._player_id = d['Results'][0]['Id']

    # --- Merlyn (Eyes camera) ---

    def teleport_merlyn(self, pos, target=None, look_angles=None):
        """Teleport Merlyn and point his Eyes camera.

        If target is given, sets AgentTarget for auto-facing.
        If look_angles is given, sets AgentLookAngles directly.
        """
        self._find_merlyn()
        props = {'AgentTeleportTo': pos}
        if target:
            props['AgentTarget'] = target
        if look_angles:
            props['AgentLookAngles'] = look_angles
        else:
            props['AgentLookAngles'] = '0,0,0'
        call('set_component', id=self._npc_comp_id, properties=props)
        time.sleep(1.5)

    def capture_merlyn(self, width=1280, height=720):
        """Capture a screenshot from Merlyn's Eyes camera."""
        self._find_merlyn()
        r = call('camera_screenshot', camera=self._cam_comp_id,
                 width=width, height=height, includeUi=False)
        for item in r['result'].get('content', []):
            if item.get('type') == 'image':
                return base64.b64decode(item['data'])
        return None

    # --- Player (main camera) ---

    def teleport_player(self, pos, angles=None):
        """Teleport the Player Controller. Camera follows automatically."""
        self._find_player()
        args = {'id': self._player_id, 'position': pos}
        if angles:
            args['angles'] = angles
        call('set_game_object', **args)
        time.sleep(1.5)

    def capture_player(self, width=1280, height=720):
        """Capture a screenshot from the player's main camera."""
        r = call('camera_screenshot', width=width, height=height, includeUi=False)
        for item in r['result'].get('content', []):
            if item.get('type') == 'image':
                return base64.b64decode(item['data'])
        return None

    # --- Flash light (for dark scenes) ---

    _flash_id = None

    def flash_on(self, position=None, radius=2000, color='1.0,0.95,0.85,1.0'):
        """Create a temporary point light to illuminate a dark scene.

        Args:
            position: "x,y,z" world position for the light. If None, uses
                      Merlyn's current position.
            radius: Light radius in world units (default 2000 = ~50m).
            color: Light color as "r,g,b,a" (default warm white).

        Returns the temporary GameObject's id, or None on failure.
        """
        if position is None:
            pos = self.get_merlyn_pos()
            if pos:
                position = f'{pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}'
            else:
                position = '0,0,500'

        r = call('create_game_object', name='VisionFlash',
                 position=position, components='PointLight')
        text = r['result']['content'][0]['text']
        match = re.search(r'"Id"\s*:\s*"([a-f0-9-]+)"', text)
        if not match:
            return None
        light_go_id = match.group(1)

        # Set light properties: large radius, warm white, no shadows
        r2 = call('find_game_objects', name='VisionFlash')
        d = json.loads(r2['result']['content'][0]['text'])
        comp_id = None
        if d.get('Results'):
            go_data = call('get_game_object', id=d['Results'][0]['Id'])
            go_text = go_data['result']['content'][0]['text']
            comp_match = re.search(r'"Type"\s*:\s*"PointLight"\s*,\s*"Id"\s*:\s*"([a-f0-9-]+)"', go_text)
            if comp_match:
                comp_id = comp_match.group(1)

        if comp_id:
            call('set_component', id=comp_id, properties={
                'Radius': str(radius),
                'LightColor': color,
                'Shadows': 'false',
            })
        else:
            # Fallback: set by game object id + type name
            call('set_component', id=light_go_id, type='PointLight', properties={
                'Radius': str(radius),
                'LightColor': color,
                'Shadows': 'false',
            })

        self._flash_id = light_go_id
        time.sleep(0.3)  # let the light take effect
        return light_go_id

    def flash_off(self):
        """Remove the temporary flash light if one is active."""
        if self._flash_id:
            try:
                call('delete_game_object', id=self._flash_id)
            except Exception:
                pass
            self._flash_id = None

    # --- Unified capture ---

    def teleport(self, pos, angles=None, target=None, via='merlyn'):
        """Teleport via merlyn or player."""
        if via == 'merlyn':
            self.teleport_merlyn(pos, target=target, look_angles=angles)
        else:
            self.teleport_player(pos, angles=angles)

    def capture(self, via='merlyn', width=1280, height=720, flash=False,
                flash_threshold=0.40, flash_radius=2000, flash_pos=None):
        """Capture via merlyn (Eyes camera) or player (main camera).

        If flash=True, takes a test shot first and checks brightness.
        If dark_pct exceeds flash_threshold, creates a temporary point
        light at flash_pos (or Merlyn's position if None), re-captures,
        then removes the light. Returns the better-lit frame.
        """
        img = self.capture_merlyn(width, height) if via == 'merlyn' else self.capture_player(width, height)
        if not img or not flash:
            return img

        ok, stats = pixel_check(img)
        if stats['dark_pct'] <= flash_threshold:
            return img  # scene is bright enough

        # Scene is too dark — flash and re-capture
        pos_str = None
        if flash_pos:
            pos_str = f'{flash_pos[0]:.0f},{flash_pos[1]:.0f},{flash_pos[2]:.0f}'
        self.flash_on(position=pos_str, radius=flash_radius)
        img2 = self.capture_merlyn(width, height) if via == 'merlyn' else self.capture_player(width, height)
        self.flash_off()

        if img2:
            ok2, stats2 = pixel_check(img2)
            if stats2['dark_pct'] < stats['dark_pct']:
                return img2  # flash improved the shot
        return img  # flash didn't help, return original

    # --- Merlyn position polling (for drive_merlyn.py) ---

    def get_merlyn_pos(self):
        """Return Merlyn's current world position as (x, y, z) tuple."""
        self._find_merlyn()
        go = call('get_game_object', id=self._merlyn_id)
        text = go['result']['content'][0]['text']
        pos = re.search(r'"WorldPosition"\s*:\s*"([0-9.,-]+)"', text)
        if not pos:
            return None
        parts = [float(x) for x in pos.group(1).split(',')]
        return tuple(parts)
