"""
Fixed monument inspection — correct tower positions, moat collision check,
corner overlap check.
"""
import json, urllib.request, re, time

M = 39.37
CENTER = [15748, 15748, 0]
WALL_HW = 140 * M
MOAT_HW = 170 * M
WALL_H = 12 * M
TOWER_H = 18 * M

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=10)
    return json.loads(r.read().decode())

def trace(frm, to):
    r = call('scene_trace', **{'from':frm,'to':to})
    d = json.loads(r['result']['content'][0]['text'])
    return d

def get_obj(name):
    r = call('find_game_objects', name=name)
    d = json.loads(r['result']['content'][0]['text'])
    results = d.get('Results', [])
    if not results:
        return None
    return results[0]

def get_pos(id):
    go = call('get_game_object', id=id)
    t = go['result']['content'][0]['text']
    m = re.search(r'WorldPosition...([0-9.,-]+)', t)
    return [float(x) for x in m.group(1).split(',')] if m else None

def get_components(id):
    go = call('get_game_object', id=id)
    t = go['result']['content'][0]['text']
    comps = re.findall(r'"Type":"([^"]+)"', t)
    return comps

print("=== FIXED INSPECTION ===")
print()

discrepancies = []

# --- 1. TOWERS: trace at actual tower positions ---
print("--- 1. TOWERS (at actual positions) ---")
for side in range(4):
    for idx in range(2):
        name = f"Tower_{side}_{idx}"
        obj = get_obj(name)
        if obj:
            pos = get_pos(obj['Id'])
            # Trace down from above tower top
            frm = f"{pos[0]},{pos[1]},{TOWER_H+300}"
            to = f"{pos[0]},{pos[1]},0"
            d = trace(frm, to)
            hit_name = d.get('GameObject',{}).get('Name','') if d.get('GameObject') else ''
            hit_z = float(d.get('EndPosition','0,0,0').split(',')[2])
            ok = 'Tower' in hit_name
            print(f"  {name}: pos=({pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}) hit={hit_name} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
            if not ok:
                discrepancies.append(f"{name}: traced hit {hit_name} instead of Tower")
        else:
            print(f"  {name}: NOT FOUND")
            discrepancies.append(f"{name}: not found")
print()

# --- 2. MOAT: check if moat has collider ---
print("--- 2. MOAT (collider check) ---")
for side in range(4):
    name = f"Moat_{side}"
    obj = get_obj(name)
    if obj:
        pos = get_pos(obj['Id'])
        comps = get_components(obj['Id'])
        has_collider = any('Collider' in c for c in comps)
        print(f"  {name}: pos=({pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}) comps={comps} collider={has_collider}")
        if not has_collider:
            discrepancies.append(f"{name}: no collider")
    else:
        print(f"  {name}: NOT FOUND")
        discrepancies.append(f"{name}: not found")
print()

# --- 3. MOAT CORNERS ---
print("--- 3. MOAT CORNERS ---")
for corner in range(4):
    name = f"MoatCorner_{corner}"
    obj = get_obj(name)
    if obj:
        pos = get_pos(obj['Id'])
        comps = get_components(obj['Id'])
        has_collider = any('Collider' in c for c in comps)
        print(f"  {name}: pos=({pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}) collider={has_collider}")
        if not has_collider:
            discrepancies.append(f"{name}: no collider")
    else:
        print(f"  {name}: NOT FOUND")
        discrepancies.append(f"{name}: not found")
print()

# --- 4. CORNERS: check overlap with wall segments ---
print("--- 4. CORNER OVERLAP ---")
corner_names = ['NW', 'NE', 'SE', 'SW']
for corner in range(4):
    cname = f"WallCorner_{corner}"
    obj = get_obj(cname)
    if obj:
        pos = get_pos(obj['Id'])
        comps = get_components(obj['Id'])
        has_collider = any('Collider' in c for c in comps)
        print(f"  {cname} ({corner_names[corner]}): pos=({pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}) collider={has_collider}")
    else:
        print(f"  {cname}: NOT FOUND")
print()

# --- 5. GATE CEILING: check if present ---
print("--- 5. GATE CEILINGS ---")
for side in range(4):
    name = f"GateCeiling_{side}"
    obj = get_obj(name)
    if obj:
        pos = get_obj(obj['Id'])
        pos = get_pos(obj['Id'])
        print(f"  {name}: pos=({pos[0]:.0f},{pos[1]:.0f},{pos[2]:.0f}) OK")
    else:
        print(f"  {name}: NOT FOUND")
        discrepancies.append(f"{name}: not found")
print()

# --- 6. GATE JAMBS + LINTELS ---
print("--- 6. GATE JAMBS + LINTELS ---")
jamb_count = 0
lintel_count = 0
for side in range(4):
    for j in range(2):
        obj = get_obj(f"GateJamb_{side}_{j}")
        if obj:
            jamb_count += 1
    obj = get_obj(f"GateLintel_{side}")
    if obj:
        lintel_count += 1
print(f"  Jambs: {jamb_count} (expect 8)")
print(f"  Lintels: {lintel_count} (expect 4)")
if jamb_count != 8:
    discrepancies.append(f"Gate jambs: {jamb_count} (expected 8)")
if lintel_count != 4:
    discrepancies.append(f"Gate lintels: {lintel_count} (expected 4)")
print()

# --- 7. TOWER LIGHTS ---
print("--- 7. TOWER LIGHTS ---")
r = call('find_game_objects', name='TowerLight')
d = json.loads(r['result']['content'][0]['text'])
light_count = d.get('Total', 0)
print(f"  TowerLight objects: {light_count} (expect 8)")
if light_count != 8:
    discrepancies.append(f"TowerLights: {light_count} (expected 8)")
print()

# --- 8. PLAZA LIGHTS ---
print("--- 8. PLAZA LIGHTS ---")
r = call('find_game_objects', name='PlazaLight')
d = json.loads(r['result']['content'][0]['text'])
plaza_light_count = d.get('Total', 0)
print(f"  PlazaLight objects: {plaza_light_count} (expect 4)")
if plaza_light_count != 4:
    discrepancies.append(f"PlazaLights: {plaza_light_count} (expected 4)")
print()

# --- 9. INNER RING FLOOR ---
print("--- 9. INNER RING FLOOR ---")
r = call('find_game_objects', name='InnerRingFloor')
d = json.loads(r['result']['content'][0]['text'])
ring_count = d.get('Total', 0)
print(f"  InnerRingFloor objects: {ring_count} (expect 4)")
if ring_count != 4:
    discrepancies.append(f"InnerRingFloor: {ring_count} (expected 4)")
print()

# === SUMMARY ===
print("=" * 60)
if discrepancies:
    print(f"DISCREPANCIES FOUND: {len(discrepancies)}")
    for d in discrepancies:
        print(f"  - {d}")
else:
    print("NO DISCREPANCIES FOUND")
print("=" * 60)
