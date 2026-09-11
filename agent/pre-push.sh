#!/bin/sh
# Pre-push hook: run deterministic verification before pushing to main.
# Runs collision probes + scene telemetry (no VLM, ~30s total).
# If any check fails, the push is blocked.
#
# To install: copy this file to .git/hooks/pre-push (or it's already
# installed via the agent setup). Make sure it's executable.
#
# To bypass temporarily: git push --no-verify (use sparingly).

echo "=== Lute Pre-Push Verification ==="

# Only run on pushes to main (or when pushing main among other branches)
# Git passes the remote and URL as stdin lines. We check the ref being pushed.
while read local_ref local_sha remote_ref remote_sha; do
    if echo "$remote_ref" | grep -q "refs/heads/main"; then
        MAIN_PUSH=1
    fi
done

if [ -z "$MAIN_PUSH" ]; then
    echo "  Not pushing main — skipping verification."
    exit 0
fi

# Check if S&Box editor is running (MCP server on 7269)
if ! python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:7269/mcp', timeout=3)" 2>/dev/null; then
    echo "  [WARN] S&Box MCP server not reachable — skipping verification."
    echo "  (Start the S&Box editor with MCP enabled to run pre-push checks.)"
    exit 0
fi

echo "Running collision probes + scene telemetry (no VLM)..."
echo ""

# Run collision probes (fast, deterministic)
python agent/collision_probes.py --json > /tmp/lute_collision.json 2>/dev/null
COLLISION_PASS=$?

# Run scene telemetry (fast, deterministic)
python agent/scene_telemetry.py --json > /tmp/lute_telemetry.json 2>/dev/null
TELEMETRY_PASS=$?

# Check results
FAILED=0

if [ $COLLISION_PASS -ne 0 ]; then
    echo "  [FAIL] Collision probes — script error"
    FAILED=1
else
    # Parse JSON for any failed checks
    COLLISION_FAILS=$(python -c "
import json
with open('/tmp/lute_collision.json') as f:
    data = json.load(f)
fails = [r['check'] for r in data if not r.get('passed', False)]
print(len(fails))
" 2>/dev/null)
    if [ "$COLLISION_FAILS" != "0" ]; then
        echo "  [FAIL] Collision probes — $COLLISION_FAILS check(s) failed"
        python -c "
import json
with open('/tmp/lute_collision.json') as f:
    data = json.load(f)
for r in data:
    if not r.get('passed', False):
        print(f'    {r[\"check\"]}')
" 2>/dev/null
        FAILED=1
    else
        echo "  [PASS] Collision probes"
    fi
fi

if [ $TELEMETRY_PASS -ne 0 ]; then
    echo "  [FAIL] Scene telemetry — script error"
    FAILED=1
else
    TELEMETRY_FAILS=$(python -c "
import json
with open('/tmp/lute_telemetry.json') as f:
    data = json.load(f)
# Count failures (hierarchy/material_coverage are WARN, not FAIL)
fails = [r['check'] for r in data
         if not r.get('passed', False) and r['check'] not in ('material_coverage', 'hierarchy')]
print(len(fails))
" 2>/dev/null)
    if [ "$TELEMETRY_FAILS" != "0" ]; then
        echo "  [FAIL] Scene telemetry — $TELEMETRY_FAILS check(s) failed"
        python -c "
import json
with open('/tmp/lute_telemetry.json') as f:
    data = json.load(f)
for r in data:
    if not r.get('passed', False) and r['check'] not in ('material_coverage', 'hierarchy'):
        print(f'    {r[\"check\"]}')
" 2>/dev/null
        FAILED=1
    else
        echo "  [PASS] Scene telemetry"
    fi
fi

echo ""
if [ $FAILED -ne 0 ]; then
    echo "=== PRE-PUSH VERIFICATION FAILED ==="
    echo "Push blocked. Fix the failing checks or use 'git push --no-verify' to bypass."
    exit 1
fi

echo "=== PRE-PUSH VERIFICATION PASSED ==="
exit 0
