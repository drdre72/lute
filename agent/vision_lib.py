"""
vision_lib.py — Shared library for S&Box camera control, screenshot capture,
and Moondream2 vision queries. Used by sbox_vision.py, sbox_camera.py, and
drive_merlyn.py.

Two capture modes:
  - merlyn: teleport Merlyn NPC, capture from his Eyes camera (over-the-shoulder)
  - player: teleport the Player Controller, capture from the main camera

Usage from other scripts:
  from vision_lib import VisonLib
  vl = VisionLib()
  vl.teleport_merlyn(pos, target=target)
  img = vl.capture_merlyn()
  answer = vl.ask_vision(img, "Describe what you see")
"""
import json, urllib.request, base64, os, sys, math, re, time, io

MCP = 'http://127.0.0.1:7269/mcp'
VISION = 'http://127.0.0.1:1234/v1/chat/completions'
MODEL = 'moondream-2b-2025-04-14'
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
    """Send an image + question to the local Moondream2 vision model.

    Converts to JPEG, resizes to 640x360, base64-encodes, and POSTs to
    the OpenAI-compatible /v1/chat/completions endpoint.

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
