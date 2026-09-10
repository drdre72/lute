"""
Look around the south gate archway from an offset position so rays
don't hit Merlyn's own body. Merlyn stands just south of the gate.
Rays cast from positions away from his body.
"""
import json, urllib.request, re

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=10)
    return json.loads(r.read().decode())

def trace(frm, to):
    r = call('scene_trace', **{'from':frm,'to':to})
    d = json.loads(r['result']['content'][0]['text'])
    hit = d.get('Hit', False)
    name = d.get('GameObject',{}).get('Name','') if d.get('GameObject') else ''
    pos = d.get('EndPosition','')
    frac = d.get('Fraction',1)
    comp = d.get('Component','')
    dist = d.get('Distance',0)
    return hit, name, pos, frac, comp, dist

# South gate center = (15748, 10236, 0)
# Tunnel runs along Y. Outside = -Y, inside(market) = +Y
cx, cy = 15748, 10236

print("=== SOUTH GATE ARCHWAY — RAY SCAN ===")
print(f"Gate center: ({cx}, {cy})")
print(f"Tunnel runs along Y. Outside = -Y, Market = +Y")
print()

# Cast all rays from positions OFFSET from Merlyn so we don't hit him.
# Merlyn is at (15748, 10236, 50). Cast from (15748, 9900, z) — 336 units south.

# --- 1. Tunnel cross-section: east-west rays at various heights ---
print("--- 1. Tunnel cross-section (E-W rays, from south of gate) ---")
print("    Shows the gate opening width and what walls frame it")
for z in [10, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500]:
    frm = f"{cx-400},{cy-300},{z}"
    to = f"{cx+400},{cy-300},{z}"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    # Parse hit position
    hx = float(pos.split(',')[0]) if pos else 0
    print(f"  z={z:3d}: hit={hit} name={name:20s} dist={dist:6.0f} frac={frac:.3f} hit_x={hx:.0f}")
print()

# --- 2. North-south through tunnel at various X offsets ---
print("--- 2. Through-tunnel rays (N-S at various X offsets) ---")
print("    Shows what blocks the tunnel at each horizontal position")
for x_off in [-250, -200, -150, -100, -50, 0, 50, 100, 150, 200, 250]:
    x = cx + x_off
    frm = f"{x},{cy-400},50"
    to = f"{x},{cy+400},50"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    hy = float(pos.split(',')[1]) if pos else 0
    print(f"  x={x_off:+4d} (x={x:.0f}): hit={hit} name={name:20s} dist={dist:6.0f} frac={frac:.3f} hit_y={hy:.0f}")
print()

# --- 3. Vertical rays at gate center (ceiling height) ---
print("--- 3. Vertical at gate center (ceiling/murder-hole) ---")
for y_off in [-200, -100, 0, 100, 200]:
    y = cy + y_off
    frm = f"{cx},{y},10"
    to = f"{cx},{y},700"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    hz = float(pos.split(',')[2]) if pos else 0
    print(f"  y_off={y_off:+4d} (y={y:.0f}): hit={hit} name={name:20s} dist={dist:6.0f} frac={frac:.3f} hit_z={hz:.0f}")
print()

# --- 4. Portcullis bars: trace through them ---
print("--- 4. Portcullis bars (at z=354, their center height) ---")
print("    Bars are at z~354, spanning z=236-472 (upper half of gate)")
for x_off in [-250, -200, -150, -100, -50, 0, 50, 100, 150, 200, 250]:
    x = cx + x_off
    # Trace north-south through the gate at bar height
    frm = f"{x},{cy-200},354"
    to = f"{x},{cy+200},354"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  x={x_off:+4d}: hit={hit} name={name:20s} dist={dist:6.0f} frac={frac:.3f}")
print()

# --- 5. Object inventory at the gate ---
print("--- 5. Objects near south gate ---")
for pattern in ['GateCeiling_2', 'GateJamb_2', 'GateLintel_2', 'PortcullisV_2', 'PortcullisH_2', 'Wall_2_0', 'Wall_2_1', 'Tower_2_0', 'Tower_2_1']:
    r = call('find_game_objects', name=pattern)
    d = json.loads(r['result']['content'][0]['text'])
    total = d.get('Total', 0)
    if total > 0 and d.get('Results'):
        first = d['Results'][0]
        go = call('get_game_object', id=first['Id'])
        t = go['result']['content'][0]['text']
        pos = re.search(r'WorldPosition...([0-9.,-]+)', t)
        scale = re.search(r'WorldScale...([0-9.,-]+)', t)
        comps = re.findall(r'"Type":"([^"]+)"', t)
        print(f"  {pattern}: count={total} pos=({pos.group(1) if pos else '?'}) scale=({scale.group(1) if scale else '?'}) comps={comps}")
print()

# --- 6. Gate dimensions summary ---
print("--- 6. Gate dimensions (from traces) ---")
# Opening width: find where E-W ray at z=50 stops hitting wall
# From section 2, the clear opening is where frac=1.0 (no hit)
# Wall thickness: from the E-W ray, distance to wall
print("  (See sections 1-2 above for measured dimensions)")
