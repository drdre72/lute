#!/usr/bin/env python3
"""Phase 17: More atmospheric detail — glowing altar runes on floor,
torch sconces on nave walls, town well bucket, more trees, cloud puffs."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

def sa(parent, type, name):
    r = add(parent, type, name)
    print(f"  +{name}: {r.get('ok')}")
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r.get('ok')}")
    return r

# ============================================================
# 1. Glowing altar runes on floor (circular pattern)
# ============================================================
print("=== Altar Floor Runes ===")

rune_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.3,'g':0.2,'b':0.05,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.5,'g':0.3,'b':0.05,'a':1},
    'emission_energy_multiplier':2.0,
    'roughness':0.3,'metallic':0.4
}

# Outer ring
sa('Architecture/NaveCombiner', 'CSGCylinder3D', 'RuneRingOuter')
ss('Architecture/NaveCombiner/RuneRingOuter', 'radius', 4.5)
ss('Architecture/NaveCombiner/RuneRingOuter', 'height', 0.04)
ss('Architecture/NaveCombiner/RuneRingOuter', 'sides', 32)
ss('Architecture/NaveCombiner/RuneRingOuter', 'position', {'x':0,'y':0.04,'z':20.0})
ss('Architecture/NaveCombiner/RuneRingOuter', 'material_override', rune_mat)

# Inner ring
sa('Architecture/NaveCombiner', 'CSGCylinder3D', 'RuneRingInner')
ss('Architecture/NaveCombiner/RuneRingInner', 'radius', 3.0)
ss('Architecture/NaveCombiner/RuneRingInner', 'height', 0.04)
ss('Architecture/NaveCombiner/RuneRingInner', 'sides', 24)
ss('Architecture/NaveCombiner/RuneRingInner', 'position', {'x':0,'y':0.04,'z':20.0})
ss('Architecture/NaveCombiner/RuneRingInner', 'material_override', rune_mat)

# Rune marks (8 small boxes around the ring)
for i in range(8):
    angle = i * math.pi / 4
    x = math.cos(angle) * 3.75
    z = 20.0 + math.sin(angle) * 3.75
    name = f'RuneMark{i}'
    sa('Architecture/NaveCombiner', 'CSGBox3D', name)
    ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.3,'y':0.06,'z':0.15})
    ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':0.05,'z':z})
    ss(f'Architecture/NaveCombiner/{name}', 'rotation_degrees', {'x':0,'y':i*45,'z':0})
    ss(f'Architecture/NaveCombiner/{name}', 'material_override', rune_mat)

print("  Altar floor runes placed!")

# ============================================================
# 2. Torch sconces on nave walls
# ============================================================
print("\n=== Wall Torch Sconces ===")

sconce_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.12,'b':0.08,'a':1},
    'roughness':0.4,'metallic':0.6
}

flame_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':1.0,'g':0.6,'b':0.1,'a':1},
    'emission_enabled':True,
    'emission':{'r':1.0,'g':0.5,'b':0.1,'a':1},
    'emission_energy_multiplier':4.0,
    'roughness':0.2
}

for side, x in [('L', -10.9), ('R', 10.9)]:
    for i, z in enumerate([15, 9, 3, -3, -9, -15]):
        name = f'Sconce_{side}{i}'
        # Bracket
        sa('Architecture/NaveCombiner', 'CSGBox3D', f'{name}_Bracket')
        ss(f'Architecture/NaveCombiner/{name}_Bracket', 'size', {'x':0.3,'y':0.8,'z':0.3})
        ss(f'Architecture/NaveCombiner/{name}_Bracket', 'position', {'x':x,'y':6.0,'z':z})
        ss(f'Architecture/NaveCombiner/{name}_Bracket', 'material_override', sconce_mat)
        
        # Flame
        sa('Architecture/NaveCombiner', 'CSGSphere3D', f'{name}_Flame')
        ss(f'Architecture/NaveCombiner/{name}_Flame', 'radius', 0.2)
        ss(f'Architecture/NaveCombiner/{name}_Flame', 'position', {'x':x,'y':6.5,'z':z})
        ss(f'Architecture/NaveCombiner/{name}_Flame', 'material_override', flame_mat)

print("  12 wall torch sconces added!")

# ============================================================
# 3. Town well bucket
# ============================================================
print("\n=== Well Bucket ===")

sa('TownArea/TownSquare', 'CSGCylinder3D', 'WellBucket')
ss('TownArea/TownSquare/WellBucket', 'radius', 0.3)
ss('TownArea/TownSquare/WellBucket', 'height', 0.5)
ss('TownArea/TownSquare/WellBucket', 'position', {'x':30.0,'y':4.0,'z':115.0})
ss('TownArea/TownSquare/WellBucket', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.18,'b':0.08,'a':1},
    'roughness':0.8
})

# Bucket rope
sa('TownArea/TownSquare', 'CSGBox3D', 'WellRope')
ss('TownArea/TownSquare/WellRope', 'size', {'x':0.05,'y':2.0,'z':0.05})
ss('TownArea/TownSquare/WellRope', 'position', {'x':30.0,'y':5.0,'z':115.0})
ss('TownArea/TownSquare/WellRope', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.15,'b':0.08,'a':1},
    'roughness':0.9
})

print("  Well bucket added!")

# ============================================================
# 4. Cloud puffs in the sky
# ============================================================
print("\n=== Clouds ===")

cloud_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.08,'b':0.12,'a':0.5},
    'roughness':1.0,'metallic':0.0,
    'transparency':2
}

random.seed(333)
for i in range(12):
    x = random.uniform(-70, 70)
    y = random.uniform(35, 55)
    z = random.uniform(40, 180)
    name = f'Cloud{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', random.uniform(5, 10))
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', cloud_mat)

print("  12 cloud puffs added!")

# ============================================================
# 5. More scattered trees on hills
# ============================================================
print("\n=== Hill Trees ===")

hill_tree_trunk = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.12,'b':0.06,'a':1},
    'roughness':0.9
}
hill_tree_leaf = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.16,'b':0.04,'a':1},
    'roughness':0.95
}

random.seed(444)
for i in range(15):
    x = random.uniform(-65, 65)
    z = random.uniform(40, 140)
    # Don't place on path or in town
    if 5 < x < 55 and 95 < z < 135:
        continue
    
    idx = 66 + i
    sa('TownArea/Terrain', 'CSGCylinder3D', f'HillTrunk{idx:02d}')
    ss(f'TownArea/Terrain/HillTrunk{idx:02d}', 'radius', 0.4)
    ss(f'TownArea/Terrain/HillTrunk{idx:02d}', 'height', 5.0)
    ss(f'TownArea/Terrain/HillTrunk{idx:02d}', 'position', {'x':x,'y':2.5,'z':z})
    ss(f'TownArea/Terrain/HillTrunk{idx:02d}', 'material_override', hill_tree_trunk)
    
    sa('TownArea/Terrain', 'CSGSphere3D', f'HillLeaves{idx:02d}')
    ss(f'TownArea/Terrain/HillLeaves{idx:02d}', 'radius', 3.0)
    ss(f'TownArea/Terrain/HillLeaves{idx:02d}', 'position', {'x':x,'y':6.5,'z':z})
    ss(f'TownArea/Terrain/HillLeaves{idx:02d}', 'material_override', hill_tree_leaf)

print("  Hill trees scattered!")

# ============================================================
# 6. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 17 complete!")
