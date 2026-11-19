import sys, re, io, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.path.insert(0, 'agent')
from mcp_client import call_tool

r = call_tool('read_console', {'limit': 3000})
lines = r.get('Lines', r.get('_raw', ''))
text = '\n'.join(str(l) for l in lines) if isinstance(lines, list) else str(lines)
all_lines = text.split('\n')
print(f"=== {len(all_lines)} lines ===\n")

pats = {
    'saved/complete': r'saved|tasks complete|Finalize|finalized|Built|completed task',
    'task_9 refs': r'task_9',
    'init': r'LuteWorld|Build\(\)|terrain|Terrain|spawned|registered|blueprint|Blueprint|tasks registered',
    'first lines': None,
}
shown = set()
for label, pat in pats.items():
    if pat is None:
        print(f'--- FIRST 30 non-spam lines ---')
        n = 0
        for l in all_lines:
            if 'ReservationConflict' in l or 'claim rejected' in l or 'could not authorize' in l or 'placement validation' in l:
                continue
            print(l[:170]); n += 1
            if n >= 30: break
        continue
    hits = [l for l in all_lines if re.search(pat, l, re.I)]
    # dedupe identical messages keeping first+last few
    uniq = []
    for h in hits:
        key = re.sub(r'\d{2}:\d{2}:\d{2}', 'T', h)[:140]
        if key not in shown:
            shown.add(key)
            uniq.append(h)
    print(f'--- {label}: {len(hits)} hits ({len(uniq)} unique) ---')
    for l in uniq[:15]:
        print(l[:170])
    if len(uniq) > 15:
        print('  ...')
        for l in uniq[-6:]:
            print(l[:170])
    print()
