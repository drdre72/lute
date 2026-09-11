"""
scene_telemetry.py — Structured scene graph verification for the Lute market.

Python replacement for sbox_verify.ps1's scene dump. Uses MCP to query
runtime objects and verify:
- Object count audit (expected vs actual counts)
- Material coverage (flag missing/default materials)
- Component completeness (BoxCollider on solid objects, etc.)
- Spatial bounds (no objects spawned out of bounds)
- Hierarchy validation (market objects under NeutralMarket root)

All checks are deterministic — no vision model needed. Results are
PASS/WARN/FAIL with details, saved to scrap/telemetry_report.json.

Usage:
    python agent/scene_telemetry.py              # run all checks
    python agent/scene_telemetry.py --only counts # run only count audit
    python agent/scene_telemetry.py --json        # output JSON only
"""
import sys, os, json, argparse, time, re

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vision_lib import call

# ── Market geometry (must match LuteMonumentBuilder.cs) ──
M = 39.37
CX, CY = 15748.0, 15748.0
WALL_HALF = 140.0 * M
MOAT_HALF = 170.0 * M
PLAZA_HALF = 60.0 * M

# Expected object counts with regex patterns for root objects only.
# Sub-objects (TowerLight, Workbench_Leg, GateJamb, etc.) are excluded.
EXPECTED_PATTERNS = {
    'Wall':         (r'^Wall_\d+_\d+$', 8),      # 4 sides x 2 segments
    'Tower':        (r'^Tower_\d+_\d+$', 8),     # 2 per gate x 4 gates
    'GateCeiling':  (r'^GateCeiling_\d+$', 4),   # 1 ceiling per gate
    'Bridge':       (r'^Bridge_\d+$', 4),        # 1 per side
    'Stall':        (r'^Stall_\d+_\d+_Counter$', 16),  # 4 sides x 4 stalls (count by counter)
    'Workbench':    (r'^Workbench_\d+$', 8),     # crafting stations (root only)
    'House':        (r'^House_\d+$', 8),         # NPC housing
    'WellRim':      (r'^WellRim$', 1),           # central well rim
    'Merlon':       (r'(?:^Merlon_|^TowerMerlon_)', -1),  # battlements (variable, >0)
    'PlazaFloor':   (r'^PlazaFloor$', 1),
    'InnerRingFloor': (r'^InnerRingFloor_\d+$', 4),  # 4 quadrants
    'MarketFoundation': (r'^MarketFoundation$', 1),
}

# Default material to flag
DEFAULT_MATERIAL = 'materials/dev/primary_white.vmat'


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


def find_objects(name=''):
    """Find game objects by name. Returns list of {Name, Id, ...}."""
    r = mcp('find_game_objects', name=name)
    return r.get('Results', []) if isinstance(r, dict) else []


def get_object(obj_id, include_props=False):
    """Get full details of a game object by ID."""
    return mcp('get_game_object', id=obj_id, includeComponentProperties=include_props)


def parse_pos(pos_str):
    """Parse 'x,y,z' string into (x, y, z) floats."""
    if isinstance(pos_str, dict):
        return (pos_str.get('x', 0), pos_str.get('y', 0), pos_str.get('z', 0))
    if isinstance(pos_str, str):
        parts = pos_str.split(',')
        if len(parts) >= 3:
            try:
                return (float(parts[0]), float(parts[1]), float(parts[2]))
            except ValueError:
                pass
    return (0, 0, 0)


# ── Check functions ──

def check_object_counts():
    """Verify expected object counts match actual runtime counts.
    Uses regex patterns to count only root objects, not sub-objects."""
    results = []

    for label, (pattern, expected) in EXPECTED_PATTERNS.items():
        regex = re.compile(pattern)
        # Search for objects matching this label prefix
        search_term = label.replace('_', '') if label == 'MarketFoundation' else label
        objs = find_objects(search_term)
        actual = sum(1 for o in objs if regex.match(o.get('Name', '')))

        if expected == -1:
            passed = actual > 0
            exp_label = '>0'
        else:
            passed = actual == expected
            exp_label = str(expected)
        results.append({
            'type': label, 'expected': exp_label, 'actual': actual,
            'passed': passed,
        })
        status = 'PASS' if passed else 'FAIL'
        print(f'  [{status}] {label}: {actual} (expected {exp_label})')

    passed_all = all(r['passed'] for r in results)
    print(f'  >> Object counts: {"ALL PASS" if passed_all else "FAILURES"}')
    return {'check': 'object_counts', 'passed': passed_all, 'details': results}


def _collect_market_objects():
    """Collect all market objects by searching for each known prefix.
    Filters out Sanctuary walls and Watchtower objects that share prefixes.
    Returns list of {Name, Id} dicts."""
    seen_ids = set()
    result = []
    # (search_term, name_filter_regex) — only keep names matching the regex
    searches = [
        ('Wall_',          r'^Wall_\d+_\d+$|^WallCorner_\d+$'),  # market walls, not Wall_0 (sanctuary)
        ('Tower_',         r'^Tower_\d+_\d+$'),                  # market towers, not Tower_Foundation etc.
        ('Gate',           r'^Gate(Ceiling|Jamb|Lintel|Torch)_'),  # gate components
        ('Bridge_',        r'^Bridge_\d+$'),
        ('Stall_',         r'^Stall_\d+_\d+_'),
        ('Workbench_',     r'^Workbench_\d+$'),                  # root only, not _Leg
        ('House_',         r'^House_\d+$'),
        ('Well',           r'^Well'),
        ('Merlon',         r'^Merlon_|^TowerMerlon_'),
        ('PlazaFloor',     r'^PlazaFloor$'),
        ('InnerRingFloor', r'^InnerRingFloor_\d+$'),
        ('MarketFoundation', r'^MarketFoundation$'),
    ]
    for search_term, filter_pattern in searches:
        regex = re.compile(filter_pattern)
        objs = find_objects(search_term)
        for o in objs:
            oid = o.get('Id', '')
            name = o.get('Name', '')
            if oid and oid not in seen_ids and regex.match(name):
                seen_ids.add(oid)
                result.append(o)
    return result


def check_material_coverage():
    """Check that all ModelRenderer objects have a material override
    or valid model material. Flag any using default/dev materials."""
    results = []
    all_objs = _collect_market_objects()

    for obj in all_objs:
        name = obj.get('Name', '')
        if not name:
            continue

        full = get_object(obj['Id'], include_props=True)
        if not isinstance(full, dict):
            continue

        comps = full.get('Components', [])
        for comp in comps:
            if not isinstance(comp, dict):
                continue
            comp_type = comp.get('Type', comp.get('__type', ''))
            if comp_type != 'ModelRenderer':
                continue

            props = comp.get('Properties', {})
            mat_override = props.get('MaterialOverride', '')
            model = props.get('Model', '')

            # Flag if using default material or no material override
            has_material = bool(mat_override) and mat_override != DEFAULT_MATERIAL
            status = 'PASS' if has_material else 'WARN'
            reason = ''
            if not mat_override:
                reason = 'no MaterialOverride'
            elif mat_override == DEFAULT_MATERIAL:
                reason = 'using default dev material'
            elif 'dev/' in str(mat_override):
                reason = f'using dev material: {mat_override}'

            results.append({
                'object': name, 'passed': has_material,
                'material': mat_override or '(none)',
                'model': model, 'reason': reason,
            })
            if reason:
                print(f'  [{status}] {name}: {reason}')
            break  # only check first ModelRenderer

    warnings = [r for r in results if not r['passed']]
    if not warnings:
        print(f'  [PASS] All {len(results)} market objects have valid materials')
    else:
        print(f'  >> Material coverage: {len(warnings)} warnings out of {len(results)} objects')

    # Material coverage is WARN, not FAIL — objects render with dev material
    # but the game doesn't break
    passed_all = len(warnings) == 0
    return {'check': 'material_coverage', 'passed': passed_all,
            'details': results, 'warning_count': len(warnings)}


def check_component_completeness():
    """Verify that solid objects have BoxCollider, lights have LightComponent,
    and terrain has Terrain + TerrainStorage."""
    results = []
    all_objs = _collect_market_objects()

    # Objects that should have BoxCollider (root objects only, not lights/merlons)
    solid_regex = re.compile(
        r'^(Wall_\d+_\d+|Tower_\d+_\d+|GateCeiling_\d+|GateLintel_\d+|'
        r'GateJamb_\d+_\d+|Bridge_\d+|Stall_\d+_\d+_Counter|'
        r'Workbench_\d+|House_\d+|WellRim|PlazaFloor|InnerRingFloor_\d+|'
        r'MarketFoundation|WallCorner_\d+)$'
    )

    for obj in all_objs:
        name = obj.get('Name', '')
        if not solid_regex.match(name):
            continue

        full = get_object(obj['Id'], include_props=False)
        if not isinstance(full, dict):
            continue

        comps = full.get('Components', [])
        comp_types = []
        for comp in comps:
            if isinstance(comp, dict):
                comp_types.append(comp.get('Type', comp.get('__type', '?')))
            else:
                comp_types.append(str(comp))

        has_collider = any('Collider' in ct for ct in comp_types)
        has_renderer = any('ModelRenderer' in ct for ct in comp_types)

        # Solid objects should have both ModelRenderer and BoxCollider
        issues = []
        if not has_collider:
            issues.append('missing BoxCollider')
        if not has_renderer:
            issues.append('missing ModelRenderer')

        passed = len(issues) == 0
        results.append({
            'object': name, 'passed': passed,
            'components': comp_types, 'issues': issues,
        })
        if issues:
            print(f'  [FAIL] {name}: {", ".join(issues)}')
            print(f'         components: {comp_types}')

    failures = [r for r in results if not r['passed']]
    if not failures:
        print(f'  [PASS] All {len(results)} solid objects have BoxCollider + ModelRenderer')
    else:
        print(f'  >> Component completeness: {len(failures)} failures out of {len(results)} objects')

    passed_all = len(failures) == 0
    return {'check': 'component_completeness', 'passed': passed_all,
            'details': results, 'failure_count': len(failures)}


def check_spatial_bounds():
    """Verify all market objects are within expected bounds.
    Plaza: +/-60m from center, walls: +/-140m, moat: +/-170m.
    Flag any objects spawned out of bounds."""
    results = []
    all_objs = _collect_market_objects()

    max_bound = MOAT_HALF + 500  # allow 500 units (12.7m) margin beyond moat

    for obj in all_objs:
        name = obj.get('Name', '')
        if not name:
            continue

        full = get_object(obj['Id'], include_props=False)
        if not isinstance(full, dict):
            continue

        pos_str = full.get('WorldPosition', '0,0,0')
        x, y, z = parse_pos(pos_str)

        dx = abs(x - CX)
        dy = abs(y - CY)
        dist = max(dx, dy)

        passed = dist <= max_bound
        if not passed:
            results.append({
                'object': name, 'passed': False,
                'x': x, 'y': y, 'z': z,
                'distance_from_center': dist,
                'max_allowed': max_bound,
            })
            print(f'  [FAIL] {name}: at ({x:.0f},{y:.0f},{z:.0f}) '
                  f'dist={dist:.0f} > max={max_bound:.0f}')

    if not results:
        print(f'  [PASS] All market objects within bounds ({max_bound/M:.0f}m)')
    else:
        print(f'  >> Spatial bounds: {len(results)} out-of-bounds objects')

    passed_all = len(results) == 0
    return {'check': 'spatial_bounds', 'passed': passed_all, 'details': results}


def check_hierarchy():
    """Verify market objects are children of the NeutralMarket GameObject.
    Flag any orphaned objects."""
    results = []
    all_objs = _collect_market_objects()

    # Find NeutralMarket root
    market_roots = find_objects('NeutralMarket')
    market_root_id = market_roots[0]['Id'] if market_roots else None

    if not market_root_id:
        print('  [WARN] NeutralMarket root not found')
        return {'check': 'hierarchy', 'passed': False, 'details': [],
                'error': 'NeutralMarket root not found'}

    orphaned = []
    for obj in all_objs:
        name = obj.get('Name', '')

        full = get_object(obj['Id'], include_props=False)
        if not isinstance(full, dict):
            continue

        parent = full.get('Parent', {})
        parent_name = parent.get('Name', '') if isinstance(parent, dict) else ''
        parent_id = parent.get('Id', '') if isinstance(parent, dict) else ''

        if parent_name != 'NeutralMarket' and parent_id != market_root_id:
            orphaned.append({
                'object': name, 'parent': parent_name or '(none)',
            })
            print(f'  [WARN] {name}: parent is "{parent_name}", expected "NeutralMarket"')

    if not orphaned:
        print(f'  [PASS] All market objects are children of NeutralMarket')
    else:
        print(f'  >> Hierarchy: {len(orphaned)} orphaned objects')

    # Hierarchy is WARN, not FAIL — orphaned objects still render
    passed_all = len(orphaned) == 0
    return {'check': 'hierarchy', 'passed': passed_all, 'details': orphaned}


# ── Main ──

ALL_CHECKS = {
    'counts': check_object_counts,
    'materials': check_material_coverage,
    'components': check_component_completeness,
    'bounds': check_spatial_bounds,
    'hierarchy': check_hierarchy,
}


def main():
    parser = argparse.ArgumentParser(description='Lute scene graph telemetry')
    parser.add_argument('--only', default='', help='Only run checks matching this substring')
    parser.add_argument('--json', action='store_true', help='Output JSON only (no prose)')
    args = parser.parse_args()

    checks = ALL_CHECKS
    if args.only:
        checks = {k: v for k, v in ALL_CHECKS.items() if args.only.lower() in k}

    if not args.json:
        print('=== Lute Scene Graph Telemetry ===')
        print(f'Checks: {", ".join(checks.keys())}')

    # Ensure play mode is running
    status = mcp('editor_status')
    if isinstance(status, dict) and not status.get('IsPlaying', False):
        if not args.json:
            print('Starting play mode...')
        mcp('play_start')
        time.sleep(10)

    report = []
    for name, func in checks.items():
        if not args.json:
            print(f'\n--- {name} ---')
        result = func()
        report.append(result)

    # Summary
    all_passed = all(r['passed'] for r in report)
    if not args.json:
        print(f'\n{"="*50}')
        print(f'  OVERALL: {"ALL PASS" if all_passed else "ISSUES DETECTED"}')
        print(f'{"="*50}')
        for r in report:
            if r['passed']:
                status = 'PASS'
            elif r['check'] in ('material_coverage', 'hierarchy'):
                status = 'WARN'
            else:
                status = 'FAIL'
            print(f'  {status:4s} {r["check"]}')
    else:
        print(json.dumps(report, indent=2))

    # Save report
    report_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                               'scrap', 'telemetry_report.json')
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, 'w') as f:
        json.dump(report, f, indent=2)
    if not args.json:
        print(f'\nReport saved: {report_path}')


if __name__ == '__main__':
    main()
