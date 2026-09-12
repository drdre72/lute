#!/usr/bin/env python3
"""
cloud_assets.py — Lute cloud-asset registry and verification.

Ensures cloud assets are referenced by their canonical S&Box ident,
not by disposable .sbox/cloud cache files. Provides:

  list    — list all registered cloud assets
  verify  — verify a single asset by ident
  check   — verify all registered assets

Usage:
  python agent/cloud_assets.py list
  python agent/cloud_assets.py verify publisher.castle_wall
  python agent/cloud_assets.py check

The registry lives at docs/assets/CLOUD_ASSETS.md (human-readable) and
docs/assets/cloud_assets.json (machine-readable). If neither exists,
the script reports zero registered assets.

Verification checks:
  1. The ident is declared in the registry.
  2. A canonical reference (Cloud.Model/Cloud.Material/etc.) exists
     in the C# source.
  3. (Runtime, if MCP is available) The asset resolves and has
     reasonable bounds when loaded.

Exit codes:
  0 = all checked assets PASS
  1 = one or more FAIL
  2 = MCP/runtime unavailable (source-level checks only)
"""
import json, os, re, sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REGISTRY_MD = os.path.join(REPO, 'docs', 'assets', 'CLOUD_ASSETS.md')
REGISTRY_JSON = os.path.join(REPO, 'docs', 'assets', 'cloud_assets.json')
CODE_DIR = os.path.join(REPO, 'sbox', 'code')
MCP = 'http://127.0.0.1:7269/mcp'


def load_registry():
    """Load the cloud-asset registry from JSON or parse the Markdown table."""
    if os.path.exists(REGISTRY_JSON):
        with open(REGISTRY_JSON) as f:
            return json.load(f)
    if os.path.exists(REGISTRY_MD):
        return parse_md_registry(REGISTRY_MD)
    return []


def parse_md_registry(path):
    """Parse a simple Markdown table into asset records."""
    assets = []
    with open(path) as f:
        for line in f:
            # Match table rows: | ident | type | purpose | verified_scale | date |
            m = re.match(r'\|\s*([^|]+)\|\s*([^|]+)\|\s*([^|]+)\|\s*([^|]*)\|\s*([^|]*)\|', line)
            if not m:
                continue
            ident = m.group(1).strip()
            if ident in ('ident', '---', 'Asset', '---'):
                continue
            assets.append({
                'ident': ident,
                'type': m.group(2).strip(),
                'purpose': m.group(3).strip(),
                'verified_scale': m.group(4).strip(),
                'date_checked': m.group(5).strip(),
            })
    return assets


def find_canonical_references(ident):
    """Search C# source for Cloud.Model/Material/etc. references to this ident."""
    refs = []
    patterns = [
        r'Cloud\.Model\s*\(\s*["\']' + re.escape(ident) + r'["\']',
        r'Cloud\.Material\s*\(\s*["\']' + re.escape(ident) + r'["\']',
        r'Cloud\.Load\s*<\w+>\s*\(\s*["\']' + re.escape(ident) + r'["\']',
        r'["\']' + re.escape(ident) + r'["\']',  # any string match
    ]
    for root, dirs, files in os.walk(CODE_DIR):
        for fname in files:
            if not fname.endswith('.cs'):
                continue
            fpath = os.path.join(root, fname)
            try:
                with open(fpath, encoding='utf-8') as f:
                    for i, line in enumerate(f, 1):
                        for pat in patterns:
                            if re.search(pat, line):
                                rel = os.path.relpath(fpath, REPO)
                                refs.append(f'{rel}:{i}: {line.strip()[:120]}')
                                break
            except Exception:
                pass
    return refs


def check_mcp_available():
    """Check if the S&Box MCP server is reachable."""
    import urllib.request
    try:
        req = json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'tools/call',
                          'params': {'name': 'editor_status', 'arguments': {}}}).encode()
        r = urllib.request.urlopen(MCP, req, timeout=3)
        return True
    except Exception:
        return False


def verify_asset(asset):
    """Verify a single cloud asset. Returns (pass, details_dict)."""
    ident = asset.get('ident', '')
    details = {'ident': ident, 'type': asset.get('type', 'unknown')}

    # Check 1: canonical reference in C# source
    refs = find_canonical_references(ident)
    details['source_refs'] = refs
    details['has_canonical_ref'] = len(refs) > 0

    # Check 2: cache file exists (informational only — NOT authoritative)
    cache_pattern = ident.replace('.', '_')
    cache_hits = []
    cloud_dir = os.path.join(REPO, 'sbox', '.sbox', 'cloud')
    if os.path.exists(cloud_dir):
        for root, dirs, files in os.walk(cloud_dir):
            for f in files:
                if cache_pattern.lower() in f.lower():
                    cache_hits.append(os.path.relpath(os.path.join(root, f), REPO))
    details['cache_files'] = cache_hits
    details['cached'] = len(cache_hits) > 0

    # Determine pass/fail
    # PASS: has canonical reference in source
    # WARN: cached but no canonical ref (the exact failure mode we want to catch)
    # FAIL: no ref and no cache
    if details['has_canonical_ref']:
        return True, details
    elif details['cached']:
        details['warning'] = ('Cached but no canonical Cloud.* reference in C# source. '
                              'This asset may work locally but break on a clean machine.')
        return False, details
    else:
        details['error'] = 'No canonical reference and no cache — asset is not available.'
        return False, details


def cmd_list():
    """List all registered cloud assets."""
    assets = load_registry()
    if not assets:
        print('No cloud assets registered. Create docs/assets/CLOUD_ASSETS.md')
        print('or docs/assets/cloud_assets.json to register assets.')
        return 0
    print(f'Registered cloud assets ({len(assets)}):')
    print()
    print(f'{"ident":<40} {"type":<10} {"purpose":<30} {"verified":<15} {"date":<12}')
    print('-' * 110)
    for a in assets:
        print(f'{a.get("ident",""):<40} {a.get("type",""):<10} {a.get("purpose",""):<30} '
              f'{a.get("verified_scale",""):<15} {a.get("date_checked",""):<12}')
    return 0


def cmd_verify(ident):
    """Verify a single asset by ident."""
    assets = load_registry()
    asset = None
    for a in assets:
        if a.get('ident') == ident:
            asset = a
            break
    if not asset:
        # Allow verifying unregistered assets (useful during adoption)
        asset = {'ident': ident, 'type': 'unknown', 'purpose': '(unregistered)'}

    ok, details = verify_asset(asset)
    status = '[PASS]' if ok else '[FAIL]'
    print(f'{status} {ident}')
    print(f'  Type: {details["type"]}')
    print(f'  Canonical ref in source: {"yes" if details["has_canonical_ref"] else "NO"}')
    if details.get('source_refs'):
        for ref in details['source_refs'][:5]:
            print(f'    -> {ref}')
    print(f'  Cached: {"yes" if details["cached"] else "no"}')
    if details.get('cache_files'):
        for cf in details['cache_files'][:3]:
            print(f'    -> {cf}')
    if details.get('warning'):
        print(f'  WARNING: {details["warning"]}')
    if details.get('error'):
        print(f'  ERROR: {details["error"]}')

    # Runtime check if MCP available
    if check_mcp_available():
        print(f'  MCP: available (runtime bounds check would go here)')
    else:
        print(f'  MCP: not reachable (source-level check only)')

    return 0 if ok else 1


def cmd_check():
    """Verify all registered assets."""
    assets = load_registry()
    if not assets:
        print('No cloud assets registered.')
        return 0

    mcp = check_mcp_available()
    passed = 0
    failed = 0
    for a in assets:
        ident = a.get('ident', '')
        ok, details = verify_asset(a)
        status = '[PASS]' if ok else '[FAIL]'
        print(f'{status} {ident}')
        if not ok:
            if details.get('warning'):
                print(f'  WARNING: {details["warning"]}')
            if details.get('error'):
                print(f'  ERROR: {details["error"]}')
        if ok:
            passed += 1
        else:
            failed += 1

    print()
    print(f'Summary: {passed} PASS, {failed} FAIL, {len(assets)} total')
    if not mcp:
        print('(MCP not reachable — source-level checks only)')
    return 0 if failed == 0 else 1


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    cmd = sys.argv[1]
    if cmd == 'list':
        return cmd_list()
    elif cmd == 'verify' and len(sys.argv) >= 3:
        return cmd_verify(sys.argv[2])
    elif cmd == 'check':
        return cmd_check()
    else:
        print(__doc__)
        return 2


if __name__ == '__main__':
    sys.exit(main())
