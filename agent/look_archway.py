"""
Look around from Merlyn's position at the south gate portcullis.
Cast rays in all directions to map the archway geometry.
South gate center = (15748, 10236, 0)
Tunnel runs along Y axis (south=negative Y, north=positive Y into market)
"""
import json, urllib.request

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

# Merlyn is at south gate: (15748, 10236, 50) — ground level, center of tunnel
# South gate tunnel runs along Y. South = -Y (outside), North = +Y (into market)
cx, cy, cz = 15748, 10236, 50

print("=== SOUTH GATE ARCHWAY INSPECTION ===")
print(f"Standing at ({cx}, {cy}, {cz}) — center of south gate tunnel")
print()

# --- Horizontal rays at eye level (z=50, ~1.3m) ---
print("--- Horizontal rays (eye level z=50) ---")
rays = [
    ("North (into market)",  f"{cx},{cy+500},50", f"{cx},{cy-500},50", "Y+"),
    ("South (outside)",     f"{cx},{cy-500},50", f"{cx},{cy+500},50", "Y-"),
    ("East (right wall)",   f"{cx+500},{cy},50", f"{cx-500},{cy},50", "X+"),
    ("West (left wall)",    f"{cx-500},{cy},50", f"{cx+500},{cy},50", "X-"),
]
for label, frm, to, direction in rays:
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  {label}: hit={hit} name={name} dist={dist:.0f} frac={frac:.3f} comp={comp}")
print()

# --- Vertical rays (up and down) ---
print("--- Vertical rays ---")
for label, frm, to in [
    ("Up (ceiling)",   f"{cx},{cy},50",  f"{cx},{cy},600"),
    ("Down (floor)",   f"{cx},{cy},50",  f"{cx},{cy},-200"),
]:
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  {label}: hit={hit} name={name} pos={pos} dist={dist:.0f} comp={comp}")
print()

# --- Rays at different heights through the tunnel (north = into market) ---
print("--- Northward rays at different heights (into market) ---")
for z in [10, 50, 100, 150, 200, 250, 300, 350, 400, 450]:
    frm = f"{cx},{cy-100},{z}"
    to = f"{cx},{cy+400},{z}"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  z={z:3d}: hit={hit} name={name} dist={dist:.0f} frac={frac:.3f}")
print()

# --- Southward rays at different heights (outside) ---
print("--- Southward rays at different heights (outside) ---")
for z in [10, 50, 100, 150, 200, 250, 300, 350, 400, 450]:
    frm = f"{cx},{cy+100},{z}"
    to = f"{cx},{cy-400},{z}"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  z={z:3d}: hit={hit} name={name} dist={dist:.0f} frac={frac:.3f}")
print()

# --- Side rays at different heights (east = right tunnel wall) ---
print("--- Eastward rays at different heights (right wall) ---")
for z in [10, 50, 100, 150, 200, 250, 300, 350, 400, 450]:
    frm = f"{cx-100},{cy},{z}"
    to = f"{cx+300},{z},{z}"  # wrong
    to = f"{cx+300},{cy},{z}"
    hit, name, pos, frac, comp, dist = trace(frm, to)
    print(f"  z={z:3d}: hit={hit} name={name} dist={dist:.0f} frac={frac:.3f}")
print()

# --- Check for portcullis bars specifically ---
print("--- Portcullis bar positions ---")
import re
r = call('find_game_objects', name='PortcullisV_2')
d = json.loads(r['result']['content'][0]['text'])
for item in d.get('Results', [])[:6]:
    go = call('get_game_object', id=item['Id'])
    t = go['result']['content'][0]['text']
    pos = re.search(r'WorldPosition...([0-9.,-]+)', t)
    scale = re.search(r'WorldScale...([0-9.,-]+)', t)
    print(f"  {item['Name']}: pos=({pos.group(1)}) scale=({scale.group(1)})")
print()

# --- Gate ceiling ---
print("--- Gate ceiling ---")
r2 = call('find_game_objects', name='GateCeiling_2')
d2 = json.loads(r2['result']['content'][0]['text'])
if d2.get('Results'):
    go = call('get_game_object', id=d2['Results'][0]['Id'])
    t = go['result']['content'][0]['text']
    pos = re.search(r'WorldPosition...([0-9.,-]+)', t)
    scale = re.search(r'WorldScale...([0-9.,-]+)', t)
    print(f"  GateCeiling_2: pos=({pos.group(1)}) scale=({scale.group(1)})")
print()

# --- Gate jambs ---
print("--- Gate jambs (south gate) ---")
for j in range(2):
    r3 = call('find_game_objects', name=f'GateJamb_2_{j}')
    d3 = json.loads(r3['result']['content'][0]['text'])
    if d3.get('Results'):
        go = call('get_game_object', id=d3['Results'][0]['Id'])
        t = go['result']['content'][0]['text']
        pos = re.search(r'WorldPosition...([0-9.,-]+)', t)
        scale = re.search(r'WorldScale...([0-9.,-]+)', t)
        print(f"  GateJamb_2_{j}: pos=({pos.group(1)}) scale=({scale.group(1)})")
print()

# --- Gate lintel ---
print("--- Gate lintel (south gate) ---")
r4 = call('find_game_objects', name='GateLintel_2')
d4 = json.loads(r4['result']['content'][0]['text'])
if d4.get('Results'):
    go = call('get_game_object', id=d4['Results'][0]['Id'])
    t = go['result']['content'][0]['text']
    pos = re.search(r'WorldPosition...([0-9.,-]+)', t)
    scale = re.search(r'WorldScale...([0-9.,-]+)', t)
    print(f"  GateLintel_2: pos=({pos.group(1)}) scale=({scale.group(1)})")
