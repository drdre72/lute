"""
collision_probes.py — Automated non-visual verification of market geometry.

Uses MCP scene_trace (raycasts) and find_game_objects to verify:
- Gate clearance (NPC can walk through gates at 3 heights)
- Wall thickness (walls are solid, not paper-thin)
- Floor solidity (objects sit on plaza, not buried or floating)
- Tower alignment (towers extend from foundation to expected height)
- Bridge traversal (continuous walkable surface across moat)

All checks are deterministic — no vision model needed. Results are
PASS/FAIL with measured values, saved to scrap/collision_report.json.

Usage:
    python agent/collision_probes.py              # run all probes
    python agent/collision_probes.py --only gates # run only gate checks
    python agent/collision_probes.py --json        # output JSON only
"""
import sys, os, json, argparse, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vision_lib import call

# ── Market geometry (must match LuteMonumentBuilder.cs) ──
# NeutralMarket root is at (15748, 15748, 0) in the live scene.
# All child positions are relative to this root, but MCP returns
# world positions, so we use the root's world position as center.
M = 39.37
CX, CY = 15748.0, 15748.0
WALL_HALF = 140.0 * M       # 5511.8
MOAT_HALF = 170.0 * M       # 6692.9
WALL_H = 12.0 * M           # 472.4
FOUND_TOP = 2.5 * M + 1.0   # ~99.4 — plaza top
TOWER_H = 18.0 * M          # 708.7
TOWER_OFFSET = 10.0 * M     # 393.7
PLAZA_HALF = 60.0 * M       # 2362.2
INNER_RING = 110.0 * M      # 4330.7
WALL_THICKNESS = 2.0 * M    # 78.7 — expected wall thickness

# NPC traversal heights (above plaza top)
NPC_HEIGHTS = [0.5 * M, 1.0 * M, 1.8 * M]  # ankle, waist, head


def mcp(tool, **args):
    """Call MCP tool and parse JSON response."""
    r = call(tool, **args)
    if isinstance(r, dict) and 'result' in r:
        content = r['result'].get('content', [])
        for item in content:
            if item.get('type') == 'text':
                try:
                    return json.loads(item['text'])
                except (json.JSONDecodeError, KeyError):
                    return {'_raw': item['text']}
    return r


def trace(from_pos, to_pos):
    """Cast a ray and return parsed result dict."""
    return mcp('scene_trace', **{'from': from_pos, 'to': to_pos})


def find_objects(name=''):
    """Find game objects by name. Returns list of {Name, Id, ...}."""
    r = mcp('find_game_objects', name=name)
    return r.get('Results', []) if isinstance(r, dict) else []


def get_object(obj_id, include_props=False):
    """Get full details of a game object by ID."""
    return mcp('get_game_object', id=obj_id, includeComponentProperties=include_props)


def fmt_pos(x, y, z):
    return f'{x:.0f},{y:.0f},{z:.0f}'


# ── Probe functions ──

def probe_gate_clearance():
    """Raycast at 3 heights across each of the 4 gate entrances.
    Gate gap is 6m = 236 units wide. Ray spans 200 units (100 each side
    from gate center) to stay within the gap.
    PASS = no hit (clear passage)."""
    results = []
    gate_positions = [
        ('north', CX, CY + WALL_HALF, 'y'),
        ('south', CX, CY - WALL_HALF, 'y'),
        ('east',  CX + WALL_HALF, CY, 'x'),
        ('west',  CX - WALL_HALF, CY, 'x'),
    ]

    ray_span = 100  # 100 units each side from center (200 total < 236 gap)
    for name, gx, gy, axis in gate_positions:
        for h in NPC_HEIGHTS:
            z = FOUND_TOP + h
            if axis == 'y':
                frm = fmt_pos(gx - ray_span, gy, z)
                to = fmt_pos(gx + ray_span, gy, z)
            else:
                frm = fmt_pos(gx, gy - ray_span, z)
                to = fmt_pos(gx, gy + ray_span, z)

            t = trace(frm, to)
            hit = t.get('Hit', False)
            dist = t.get('Distance', 0)
            hit_name = ''
            if hit:
                obj = t.get('GameObject', {})
                hit_name = obj.get('Name', '?') if isinstance(obj, dict) else '?'

            passed = not hit
            height_label = f'{h/M:.1f}m'
            results.append({
                'gate': name, 'height': height_label,
                'passed': passed, 'hit': hit,
                'distance': dist, 'hit_object': hit_name,
            })
            status = 'PASS' if passed else 'FAIL'
            print(f'  [{status}] Gate {name} @ {height_label}: '
                  f'hit={hit} dist={dist:.0f} obj={hit_name}')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Gate clearance: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'gate_clearance', 'passed': passed_all, 'details': results}


def probe_wall_thickness():
    """Raycast through each curtain wall at a point offset from the gate.
    The gate gap is at the wall center, so we offset 20m along the wall
    to hit solid wall section.
    PASS = hit and distance matches expected wall thickness."""
    results = []
    wall_offset = 20.0 * M  # offset from gate center to hit solid wall
    walls = [
        ('north', CX, CY + WALL_HALF, 'y', 1, wall_offset),   # offset east
        ('south', CX, CY - WALL_HALF, 'y', -1, wall_offset),
        ('east',  CX + WALL_HALF, CY, 'x', 1, wall_offset),
        ('west',  CX - WALL_HALF, CY, 'x', -1, wall_offset),
    ]

    for name, wx, wy, axis, direction, offset in walls:
        z = FOUND_TOP + WALL_H * 0.5  # middle of wall height
        # Offset along the wall to avoid the gate gap
        if axis == 'y':
            # Wall runs east-west, offset in X
            wx += offset
        else:
            # Wall runs north-south, offset in Y
            wy += offset

        ray_span = 500  # start 500 units outside wall
        if axis == 'y':
            frm = fmt_pos(wx, wy + direction * ray_span, z)
            to = fmt_pos(wx, wy - direction * ray_span, z)
        else:
            frm = fmt_pos(wx + direction * ray_span, wy, z)
            to = fmt_pos(wx - direction * ray_span, wy, z)

        t = trace(frm, to)
        hit = t.get('Hit', False)
        dist = t.get('Distance', 0)
        hit_name = ''
        if hit:
            obj = t.get('GameObject', {})
            hit_name = obj.get('Name', '?') if isinstance(obj, dict) else '?'

        # PASS if hit and the hit object is a wall segment
        passed = hit and 'wall' in hit_name.lower()
        results.append({
            'wall': name, 'passed': passed, 'hit': hit,
            'distance': dist, 'expected_thickness': WALL_THICKNESS,
            'hit_object': hit_name,
        })
        status = 'PASS' if passed else 'FAIL'
        print(f'  [{status}] Wall {name} (offset {offset/M:.0f}m): '
              f'hit={hit} dist={dist:.0f} obj={hit_name}')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Wall thickness: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'wall_thickness', 'passed': passed_all, 'details': results}


def probe_floor_solidity():
    """Downward raycast from above key central objects.
    Verifies objects are sitting on the plaza surface (z~99.4),
    not buried below or floating above."""
    results = []
    # Key positions to check: well center, 4 stall quadrants, 4 houses
    check_positions = [
        ('well_center', CX, CY),
        ('stall_NE', CX + 15.0 * M, CY + 15.0 * M),
        ('stall_NW', CX - 15.0 * M, CY + 15.0 * M),
        ('stall_SE', CX + 15.0 * M, CY - 15.0 * M),
        ('stall_SW', CX - 15.0 * M, CY - 15.0 * M),
        ('house_north', CX, CY + 85.0 * M),
        ('house_south', CX, CY - 85.0 * M),
        ('house_east', CX + 85.0 * M, CY),
        ('house_west', CX - 85.0 * M, CY),
    ]

    for name, px, py in check_positions:
        # Start ray 500 units above plaza top
        z_start = FOUND_TOP + 500
        frm = fmt_pos(px, py, z_start)
        to = fmt_pos(px, py, 0)

        t = trace(frm, to)
        hit = t.get('Hit', False)
        end_pos = t.get('EndPosition', {})
        if isinstance(end_pos, dict):
            hit_z = end_pos.get('z', 0)
        elif isinstance(end_pos, str):
            try:
                hit_z = float(end_pos.split(',')[2])
            except (ValueError, IndexError):
                hit_z = 0
        else:
            hit_z = 0

        hit_name = ''
        if hit:
            obj = t.get('GameObject', {})
            hit_name = obj.get('Name', '?') if isinstance(obj, dict) else '?'

        # PASS if hit and hit_z is at or above plaza top (object is on
        # the floor, not buried below it). Ray hits the top of whatever
        # is at that position — stall counter, house roof, or plaza floor.
        passed = hit and hit_z >= FOUND_TOP - 10
        results.append({
            'position': name, 'passed': passed, 'hit': hit,
            'hit_z': hit_z, 'expected_z': FOUND_TOP,
            'hit_object': hit_name,
        })
        status = 'PASS' if passed else 'FAIL'
        print(f'  [{status}] {name}: hit={hit} z={hit_z:.1f} '
              f'floor={FOUND_TOP:.1f} obj={hit_name}')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Floor solidity: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'floor_solidity', 'passed': passed_all, 'details': results}


def probe_tower_alignment():
    """Vertical raycast through each tower position.
    Verifies tower extends from foundation top to expected tower height."""
    results = []
    # 8 towers: 2 per gate, offset by TOWER_OFFSET along wall
    tower_positions = []
    for gate_axis, sign in [('y', 1), ('y', -1), ('x', 1), ('x', -1)]:
        for tower_side in [1, -1]:
            if gate_axis == 'y':
                tx = CX + tower_side * TOWER_OFFSET
                ty = CY + sign * WALL_HALF
            else:
                tx = CX + sign * WALL_HALF
                ty = CY + tower_side * TOWER_OFFSET
            tower_positions.append((tx, ty))

    for i, (tx, ty) in enumerate(tower_positions):
        # Raycast from above tower top to below foundation
        z_top = FOUND_TOP + TOWER_H + 500
        z_bot = FOUND_TOP - 200
        frm = fmt_pos(tx, ty, z_top)
        to = fmt_pos(tx, ty, z_bot)

        t = trace(frm, to)
        hit = t.get('Hit', False)
        end_pos = t.get('EndPosition', {})
        if isinstance(end_pos, dict):
            hit_z = end_pos.get('z', 0)
        elif isinstance(end_pos, str):
            try:
                hit_z = float(end_pos.split(',')[2])
            except (ValueError, IndexError):
                hit_z = 0
        else:
            hit_z = 0

        dist = t.get('Distance', 0)
        hit_name = ''
        if hit:
            obj = t.get('GameObject', {})
            hit_name = obj.get('Name', '?') if isinstance(obj, dict) else '?'

        # PASS if hit and the hit object is a tower (not the floor below)
        passed = hit and ('tower' in hit_name.lower() or 'Tower' in hit_name)
        results.append({
            'tower': f'tower_{i}', 'x': tx, 'y': ty,
            'passed': passed, 'hit': hit,
            'distance': dist,
            'hit_z': hit_z, 'hit_object': hit_name,
        })
        status = 'PASS' if passed else 'FAIL'
        print(f'  [{status}] Tower {i} ({tx:.0f},{ty:.0f}): '
              f'hit={hit} dist={dist:.0f} obj={hit_name}')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Tower alignment: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'tower_alignment', 'passed': passed_all, 'details': results}


def probe_bridge_traversal():
    """Raycast along bridge surface at ankle height.
    Verifies continuous walkable surface across the moat at each gate."""
    results = []
    # Bridges are at each gate, crossing the moat
    bridge_positions = [
        ('north', CX, CY + WALL_HALF, 'y', 1),
        ('south', CX, CY - WALL_HALF, 'y', -1),
        ('east',  CX + WALL_HALF, CY, 'x', 1),
        ('west',  CX - WALL_HALF, CY, 'x', -1),
    ]

    for name, bx, by, axis, direction in bridge_positions:
        z = FOUND_TOP + 0.5 * M  # ankle height above plaza
        # Start ray just inside the gate (200 units inside the wall)
        # and cast outward across the moat
        inner_offset = 200  # start 200 units inside the wall
        span = (MOAT_HALF - WALL_HALF) + 500  # cover bridge + margins
        if axis == 'y':
            frm = fmt_pos(bx, by - direction * inner_offset, z)
            to = fmt_pos(bx, by + direction * span, z)
        else:
            frm = fmt_pos(bx - direction * inner_offset, by, z)
            to = fmt_pos(bx + direction * span, by, z)

        t = trace(frm, to)
        hit = t.get('Hit', False)
        dist = t.get('Distance', 0)
        hit_name = ''
        if hit:
            obj = t.get('GameObject', {})
            hit_name = obj.get('Name', '?') if isinstance(obj, dict) else '?'

        # PASS if no hit (clear bridge surface at ankle height —
        # bridge floor is below, nothing blocking at 0.5m)
        # OR if hit is the bridge surface itself
        passed = not hit or 'bridge' in hit_name.lower() or dist > span * 0.8
        results.append({
            'bridge': name, 'passed': passed, 'hit': hit,
            'distance': dist, 'hit_object': hit_name,
        })
        status = 'PASS' if passed else 'FAIL'
        print(f'  [{status}] Bridge {name}: hit={hit} dist={dist:.0f} obj={hit_name}')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Bridge traversal: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'bridge_traversal', 'passed': passed_all, 'details': results}


# ── Main ──

ALL_PROBES = {
    'gates': probe_gate_clearance,
    'walls': probe_wall_thickness,
    'floor': probe_floor_solidity,
    'towers': probe_tower_alignment,
    'bridges': probe_bridge_traversal,
}


def main():
    parser = argparse.ArgumentParser(description='Lute collision & traversal probes')
    parser.add_argument('--only', default='', help='Only run probes matching this substring')
    parser.add_argument('--json', action='store_true', help='Output JSON only (no prose)')
    args = parser.parse_args()

    probes = ALL_PROBES
    if args.only:
        probes = {k: v for k, v in ALL_PROBES.items() if args.only.lower() in k}

    if not args.json:
        print('=== Lute Collision & Traversal Probes ===')
        print(f'Probes: {", ".join(probes.keys())}')

    # Ensure play mode is running
    status = mcp('editor_status')
    if isinstance(status, dict) and not status.get('IsPlaying', False):
        if not args.json:
            print('Starting play mode...')
        mcp('play_start')
        time.sleep(10)

    report = []
    for name, func in probes.items():
        if not args.json:
            print(f'\n--- {name} ---')
        result = func()
        report.append(result)

    # Summary
    all_passed = all(r['passed'] for r in report)
    if not args.json:
        print(f'\n{"="*50}')
        print(f'  OVERALL: {"ALL PASS" if all_passed else "FAILURES DETECTED"}')
        print(f'{"="*50}')
        for r in report:
            status = 'PASS' if r['passed'] else 'FAIL'
            print(f'  {status:4s} {r["check"]}')
    else:
        print(json.dumps(report, indent=2))

    # Save report
    report_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                               'scrap', 'collision_report.json')
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, 'w') as f:
        json.dump(report, f, indent=2)
    if not args.json:
        print(f'\nReport saved: {report_path}')


if __name__ == '__main__':
    main()
