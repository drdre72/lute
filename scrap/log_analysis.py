import sys, re, io, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.path.insert(0, 'agent')
from mcp_client import call_tool

limit = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
r = call_tool('read_console', {'limit': limit})
lines = r.get('Lines', r.get('_raw', ''))
if isinstance(lines, list):
    text = '\n'.join(str(l) for l in lines)
else:
    text = str(lines)
all_lines = text.split('\n')
print(f"=== {len(all_lines)} lines returned ===\n")

# categorize
cats = collections.Counter()
interesting = []
for l in all_lines:
    if 'ReservationConflict' in l or 'claim rejected' in l or 'placement validation failed' in l:
        cats['conflict_spam'] += 1
        continue
    if 'Lute:' not in l:
        continue
    if re.search(r'error|exception|fail|warn', l, re.I):
        cats['errorish'] += 1
        interesting.append(l)
    elif re.search(r'placed|brick|finaliz|complete|built|task_\d+|wall|spawn|craft|haul|deliver|finish', l, re.I):
        cats['progress'] += 1
        interesting.append(l)
    else:
        cats['other_lute'] += 1
        interesting.append(l)

print('categories:', dict(cats))
print('\n=== non-spam Lute lines (last 80) ===')
for l in interesting[-80:]:
    print(l[:175])
