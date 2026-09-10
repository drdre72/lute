"""Add MatStoneTower constant to LuteMonumentBuilder.cs"""
path = r'C:\Users\Shadow\Documents\lute\sbox\code\LuteMonumentBuilder.cs'
with open(path, 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()

for i, line in enumerate(lines):
    if 'const string MatStoneWall' in line:
        new_line = '\tconst string MatStoneTower  = "materials/medieval/stone_tower.vmat";  // towers (perfect brick scale)\n'
        lines.insert(i + 1, new_line)
        print(f'Inserted MatStoneTower after line {i+1}')
        break

with open(path, 'w', encoding='utf-8') as f:
    f.writelines(lines)
print('Done')
