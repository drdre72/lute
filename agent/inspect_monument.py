"""
Systematic monument inspection.

Teleports Merlyn to each major feature of the Neutral Market and uses
scene_trace + get_game_object to verify geometry. Reports discrepancies.

Monument center: (15748, 15748, 0)
WallOuterHalfWidth = 140m = 5511.8 units
MoatOuterHalfWidth = 170m = 6692.9 units
WallHeight = 12m = 472.4 units
TowerHeight = 18m = 708.6 units
"""
import json, urllib.request, re, time

M = 39.37
CENTER = [15748, 15748, 0]
WALL_HW = 140 * M      # 5511.8
MOAT_HW = 170 * M     # 6692.9
WALL_H = 12 * M       # 472.4
TOWER_H = 18 * M      # 708.6
GATE_GAP = 6 * M      # 236.2

def call(tool, **args):
    req = json.dumps({'jsonrpc':'2.0','id':1,'method':'tools/call',
                      'params':{'name':tool,'arguments':args}}).encode()
    r = urllib.request.urlopen('http://127.0.0.1:7269/mcp', req, timeout=10)
    return json.loads(r.read().decode())

def trace(frm, to):
    """Cast a ray, return (hit, hit_name, hit_pos, fraction)."""
    r = call('scene_trace', **{'from':frm,'to':to})
    text = r['result']['content'][0]['text']
    d = json.loads(text)
    hit = d.get('Hit', False)
    name = d.get('GameObject',{}).get('Name','') if d.get('GameObject') else ''
    pos = d.get('EndPosition','')
    frac = d.get('Fraction',1)
    comp = d.get('Component','')
    return hit, name, pos, frac, comp

def find_merlyn():
    r = call('find_game_objects', name='Merlyn')
    text = r['result']['content'][0]['text']
    m = re.search(r'Id...([a-f0-9-]+)', text)
    go_id = m.group(1)
    go = call('get_game_object', id=go_id)
    t = go['result']['content'][0]['text']
    cm = re.search(r'LuteBuilderNpc.*?Id...([a-f0-9-]+)', t)
    comp_id = cm.group(1)
    return go_id, comp_id

def teleport(comp_id, x, y, z):
    call('set_component', id=comp_id, properties={'AgentTeleportTo': f'{x},{y},{z}'})
    time.sleep(1)

def get_pos(go_id):
    go = call('get_game_object', id=go_id)
    t = go['result']['content'][0]['text']
    m = re.search(r'WorldPosition...([0-9.,-]+)', t)
    return [float(x) for x in m.group(1).split(',')]

def vstr(v):
    return f"{v[0]:.0f},{v[1]:.0f},{v[2]:.0f}"

# Edge centers (relative to monument center)
def edge_center(side, hw):
    cx, cy, cz = CENTER
    if side == 0:   return [cx, cy + hw, cz]  # North
    if side == 1:   return [cx + hw, cy, cz]  # East
    if side == 2:   return [cx, cy - hw, cz]  # South
    return [cx - hw, cy, cz]                  # West

SIDE_NAMES = ['North', 'East', 'South', 'West']

# Tower positions: 2 per side, offset from gate by towerOffset=10m
TOWER_OFFSET = 10 * M
def tower_pos(side, idx):
    ec = edge_center(side, WALL_HW)
    # Towers are offset along the wall from the gate center
    sign = 1 if idx == 0 else -1
    if side in (0, 2):  # N/S walls run along X
        ec[0] += sign * TOWER_OFFSET * 3  # further out than gate
    else:  # E/W walls run along Y
        ec[1] += sign * TOWER_OFFSET * 3
    ec[2] = TOWER_H * 0.5
    return ec

# === MAIN INSPECTION ===
print("=== NEUTRAL MARKET SYSTEMATIC INSPECTION ===")
print(f"Monument center: {CENTER}")
print()

go_id, comp_id = find_merlyn()
print(f"Merlyn: go={go_id} comp={comp_id}")
print()

discrepancies = []

# --- 1. GATES: trace through each gate opening ---
print("--- 1. GATES (trace through opening) ---")
for side in range(4):
    ec = edge_center(side, WALL_HW)
    name = SIDE_NAMES[side]
    # Trace from outside to inside through the gate at z=50 (ground level)
    if side == 0:  # North: trace from +Y to -Y
        frm = f"{ec[0]},{ec[1]+200},50"
        to = f"{ec[0]},{ec[1]-200},50"
    elif side == 1:  # East: trace from +X to -X
        frm = f"{ec[0]+200},{ec[1]},50"
        to = f"{ec[0]-200},{ec[1]},50"
    elif side == 2:  # South: trace from -Y to +Y
        frm = f"{ec[0]},{ec[1]-200},50"
        to = f"{ec[0]},{ec[1]+200},50"
    else:  # West: trace from -X to +X
        frm = f"{ec[0]-200},{ec[1]},50"
        to = f"{ec[0]+200},{ec[1]},50"
    hit, hname, hpos, frac, comp = trace(frm, to)
    blocked = hit and 'Wall' in hname and frac < 0.95
    status = "BLOCKED" if blocked else "clear"
    print(f"  {name} gate: {status} (hit={hit} name={hname} frac={frac:.3f})")
    if blocked:
        discrepancies.append(f"{name} gate blocked by {hname} (frac={frac:.3f})")
print()

# --- 2. WALLS: trace down onto each wall top ---
print("--- 2. WALLS (trace down onto wall top) ---")
for side in range(4):
    ec = edge_center(side, WALL_HW)
    name = SIDE_NAMES[side]
    # Offset from gate center to hit solid wall (not gate gap)
    if side in (0, 2):
        x = ec[0] + 2000  # offset along wall
        y = ec[1]
    else:
        x = ec[0]
        y = ec[1] + 2000
    # Trace down from above wall top
    frm = f"{x},{y},{WALL_H+200}"
    to = f"{x},{y},0"
    hit, hname, hpos, frac, comp = trace(frm, to)
    # Should hit wall at z ~ WALL_H
    hit_z = float(hpos.split(',')[2]) if hpos else 0
    ok = hit and 'Wall' in hname and abs(hit_z - WALL_H) < 50
    print(f"  {name} wall: hit={hit} name={hname} z={hit_z:.0f} (expect ~{WALL_H:.0f}) {'OK' if ok else 'CHECK'}")
    if not ok:
        discrepancies.append(f"{name} wall: hit={hname} z={hit_z:.0f} expected ~{WALL_H:.0f}")
print()

# --- 3. CORNERS: verify present ---
print("--- 3. CORNERS ---")
corner_names = ['NW', 'NE', 'SE', 'SW']
for corner in range(4):
    # Corner positions
    cx, cy = CENTER[0], CENTER[1]
    if corner == 0: cx -= WALL_HW; cy += WALL_HW  # NW
    elif corner == 1: cx += WALL_HW; cy += WALL_HW  # NE
    elif corner == 2: cx += WALL_HW; cy -= WALL_HW  # SE
    else: cx -= WALL_HW; cy -= WALL_HW  # SW
    frm = f"{cx},{cy},{WALL_H+200}"
    to = f"{cx},{cy},0"
    hit, hname, hpos, frac, comp = trace(frm, to)
    hit_z = float(hpos.split(',')[2]) if hpos else 0
    ok = hit and 'Corner' in hname
    print(f"  {corner_names[corner]} corner: hit={hit} name={hname} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
    if not ok:
        discrepancies.append(f"{corner_names[corner]} corner: {hname}")
print()

# --- 4. TOWERS: verify present at top ---
print("--- 4. TOWERS (trace down at tower top) ---")
for side in range(4):
    for idx in range(2):
        tp = tower_pos(side, idx)
        name = f"{SIDE_NAMES[side]} tower {idx}"
        frm = f"{tp[0]},{tp[1]},{TOWER_H+200}"
        to = f"{tp[0]},{tp[1]},0"
        hit, hname, hpos, frac, comp = trace(frm, to)
        hit_z = float(hpos.split(',')[2]) if hpos else 0
        ok = hit and 'Tower' in hname
        print(f"  {name}: hit={hit} name={hname} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
        if not ok:
            discrepancies.append(f"{name}: {hname} z={hit_z:.0f}")
print()

# --- 5. BRIDGES: verify walkable surface ---
print("--- 5. BRIDGES (trace down onto bridge deck) ---")
mid_radius = (WALL_HW + MOAT_HW) * 0.5
for side in range(4):
    ec = edge_center(side, mid_radius)
    name = SIDE_NAMES[side]
    frm = f"{ec[0]},{ec[1]},200"
    to = f"{ec[0]},{ec[1]},-100"
    hit, hname, hpos, frac, comp = trace(frm, to)
    hit_z = float(hpos.split(',')[2]) if hpos else 0
    ok = hit and 'Bridge' in hname
    print(f"  {name} bridge: hit={hit} name={hname} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
    if not ok:
        discrepancies.append(f"{name} bridge: {hname} z={hit_z:.0f}")
print()

# --- 6. MOAT: verify present (trace should NOT hit or hit moat floor) ---
print("--- 6. MOAT (trace down at moat center) ---")
for side in range(4):
    ec = edge_center(side, MOAT_HW - 100)
    name = SIDE_NAMES[side]
    frm = f"{ec[0]},{ec[1]},100"
    to = f"{ec[0]},{ec[1]},-500"
    hit, hname, hpos, frac, comp = trace(frm, to)
    hit_z = float(hpos.split(',')[2]) if hpos else 0
    ok = hit and 'Moat' in hname
    print(f"  {name} moat: hit={hit} name={hname} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
    if not ok:
        discrepancies.append(f"{name} moat: {hname} z={hit_z:.0f}")
print()

# --- 7. PLAZA FLOOR: trace down at center ---
print("--- 7. PLAZA FLOOR ---")
frm = f"{CENTER[0]},{CENTER[1]},200"
to = f"{CENTER[0]},{CENTER[1]},-100"
hit, hname, hpos, frac, comp = trace(frm, to)
hit_z = float(hpos.split(',')[2]) if hpos else 0
ok = hit and 'Plaza' in hname
print(f"  Plaza center: hit={hit} name={hname} z={hit_z:.0f} {'OK' if ok else 'CHECK'}")
if not ok:
    discrepancies.append(f"Plaza floor: {hname} z={hit_z:.0f}")
print()

# --- 8. WELL: verify present at center ---
print("--- 8. WELL ---")
r = call('find_game_objects', name='CentralWell')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
well_count = int(m.group(1)) if m else 0
print(f"  CentralWell objects: {well_count} {'OK' if well_count > 0 else 'MISSING'}")
if well_count == 0:
    discrepancies.append("CentralWell missing")
print()

# --- 9. STALLS: count ---
print("--- 9. STALLS ---")
r = call('find_game_objects', name='Stall')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
stall_count = int(m.group(1)) if m else 0
print(f"  Stall objects: {stall_count} (expect 32 = 16 counters + 16 awnings)")
if stall_count != 32:
    discrepancies.append(f"Stalls: {stall_count} (expected 32)")
print()

# --- 10. WORKBENCHES: count ---
print("--- 10. WORKBENCHES ---")
r = call('find_game_objects', name='Workbench')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
wb_count = int(m.group(1)) if m else 0
print(f"  Workbench objects: {wb_count} (expect 8 benches + 32 legs = 40)")
if wb_count != 40:
    discrepancies.append(f"Workbenches: {wb_count} (expected 40)")
print()

# --- 11. HOUSES: count ---
print("--- 11. HOUSES ---")
r = call('find_game_objects', name='House')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
house_count = int(m.group(1)) if m else 0
print(f"  House objects: {house_count} (expect 8)")
if house_count != 8:
    discrepancies.append(f"Houses: {house_count} (expected 8)")
print()

# --- 12. MERLON/BATTLEMENT count ---
print("--- 12. BATTLEMENTS ---")
r = call('find_game_objects', name='Merlon')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
merlon_count = int(m.group(1)) if m else 0
print(f"  Merlon objects: {merlon_count} (404 wall + 32 tower = 436 expected)")
if merlon_count != 436:
    discrepancies.append(f"Merlons: {merlon_count} (expected 436)")
print()

# --- 13. PORTCULLIS count ---
print("--- 13. PORTCULLIS ---")
r = call('find_game_objects', name='Portcullis')
text = r['result']['content'][0]['text']
m = re.search(r'Total..(\d+)', text)
port_count = int(m.group(1)) if m else 0
print(f"  Portcullis objects: {port_count} (expect 36 = 24 vert + 12 horiz)")
if port_count != 36:
    discrepancies.append(f"Portcullis: {port_count} (expected 36)")
print()

# === SUMMARY ===
print("=" * 60)
if discrepancies:
    print(f"DISCREPANCIES FOUND: {len(discrepancies)}")
    for d in discrepancies:
        print(f"  - {d}")
else:
    print("NO DISCREPANCIES FOUND - all checks passed")
print("=" * 60)
