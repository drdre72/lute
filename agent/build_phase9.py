#!/usr/bin/env python3
"""Phase 9: Fix well roof, add wall buttresses, banners, candle holders,
graveyard, path extension to forest, more atmospheric detail."""
import sys, os, math
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
# 1. Fix well roof — use cylinder with low sides instead of cone
# ============================================================
print("=== Fix Well Roof ===")

sa('TownArea/TownSquare', 'CSGCylinder3D', 'WellRoofFix')
ss('TownArea/TownSquare/WellRoofFix', 'radius', 2.5)
ss('TownArea/TownSquare/WellRoofFix', 'height', 2.0)
ss('TownArea/TownSquare/WellRoofFix', 'sides', 8)
ss('TownArea/TownSquare/WellRoofFix', 'position', {'x':30.0,'y':5.0,'z':115.0})
ss('TownArea/TownSquare/WellRoofFix', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.12,'b':0.08,'a':1},
    'roughness':0.8
})

print("  Well roof fixed!")

# ============================================================
# 2. Nave buttresses (exterior support pillars)
# ============================================================
print("\n=== Nave Buttresses ===")

buttress_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.28,'b':0.25,'a':1},
    'roughness':0.8,'metallic':0.05
}

for side, x in [('L', -12.0), ('R', 12.0)]:
    for i, z in enumerate([15, 6, -3, -12]):
        name = f'Buttress_{side}{i}'
        sa('Architecture/NaveCombiner', 'CSGBox3D', name)
        ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':1.5,'y':15.0,'z':1.5})
        ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':7.5,'z':z})
        ss(f'Architecture/NaveCombiner/{name}', 'material_override', buttress_mat)

print("  8 buttresses added!")

# ============================================================
# 3. Wall banners (tapestries hanging between pillars)
# ============================================================
print("\n=== Wall Banners ===")

banner_colors = [
    {'r':0.5,'g':0.1,'b':0.1,'a':1},  # red
    {'r':0.1,'g':0.2,'b':0.5,'a':1},  # blue
    {'r':0.3,'g':0.2,'b':0.5,'a':1},  # purple
]

for side, x in [('L', -10.8), ('R', 10.8)]:
    for i, z in enumerate([12, 0, -12]):
        color = banner_colors[i % 3]
        name = f'Banner_{side}{i}'
        sa('Architecture/NaveCombiner', 'CSGBox3D', name)
        ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.1,'y':8.0,'z':3.0})
        ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':7.0,'z':z})
        ss(f'Architecture/NaveCombiner/{name}', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':color,
            'roughness':0.9,'metallic':0.0,
            'emission_enabled':True,
            'emission':color,
            'emission_energy_multiplier':0.15
        })

print("  6 banners hung!")

# ============================================================
# 4. Candle holders on pillar capitals
# ============================================================
print("\n=== Candle Holders ===")

for side in ['L', 'R']:
    for i in range(6):
        name = f'Candle_{side}{i}'
        pillar_path = f'Architecture/NaveCombiner/Pillar_{side}{i}'
        
        # Candle holder base
        sa(pillar_path, 'CSGCylinder3D', f'{name}_Base')
        ss(f'{pillar_path}/{name}_Base', 'radius', 0.3)
        ss(f'{pillar_path}/{name}_Base', 'height', 0.2)
        ss(f'{pillar_path}/{name}_Base', 'position', {'x':0,'y':7.7,'z':0})
        ss(f'{pillar_path}/{name}_Base', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':{'r':0.15,'g':0.12,'b':0.08,'a':1},
            'roughness':0.3,'metallic':0.7
        })
        
        # Candle
        sa(pillar_path, 'CSGCylinder3D', f'{name}_Wax')
        ss(f'{pillar_path}/{name}_Wax', 'radius', 0.08)
        ss(f'{pillar_path}/{name}_Wax', 'height', 0.4)
        ss(f'{pillar_path}/{name}_Wax', 'position', {'x':0,'y':8.0,'z':0})
        ss(f'{pillar_path}/{name}_Wax', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':{'r':0.9,'g':0.85,'b':0.7,'a':1},
            'roughness':0.6
        })
        
        # Flame
        sa(pillar_path, 'CSGSphere3D', f'{name}_Flame')
        ss(f'{pillar_path}/{name}_Flame', 'radius', 0.1)
        ss(f'{pillar_path}/{name}_Flame', 'position', {'x':0,'y':8.25,'z':0})
        ss(f'{pillar_path}/{name}_Flame', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':{'r':1.0,'g':0.6,'b':0.1,'a':1},
            'emission_enabled':True,
            'emission':{'r':1.0,'g':0.5,'b':0.1,'a':1},
            'emission_energy_multiplier':3.0,
            'roughness':0.2
        })

print("  12 candle holders on pillars!")

# ============================================================
# 5. Graveyard behind town
# ============================================================
print("\n=== Graveyard ===")

add('TownArea', 'Node3D', 'Graveyard')
add('TownArea/Graveyard', 'CSGCombiner3D', 'GraveyardTerrain')

# Ground
sa('TownArea/Graveyard/GraveyardTerrain', 'CSGBox3D', 'GraveyardGround')
ss('TownArea/Graveyard/GraveyardTerrain/GraveyardGround', 'size', {'x':30.0,'y':0.3,'z':20.0})
ss('TownArea/Graveyard/GraveyardTerrain/GraveyardGround', 'position', {'x':30.0,'y':0.15,'z':150.0})
ss('TownArea/Graveyard/GraveyardTerrain/GraveyardGround', 'use_collision', True)
ss('TownArea/Graveyard/GraveyardTerrain/GraveyardGround', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.12,'g':0.14,'b':0.10,'a':1},
    'roughness':0.95,
    'uv1_scale':{'x':8,'y':8,'z':8}
})

# Tombstones
grave_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.23,'b':0.22,'a':1},
    'roughness':0.9,'metallic':0.03
}

grave_positions = [
    (22, 145), (26, 145), (34, 145), (38, 145),
    (22, 150), (26, 150), (34, 150), (38, 150),
    (22, 155), (26, 155), (34, 155), (38, 155),
]

for i, (x, z) in enumerate(grave_positions):
    name = f'Tombstone{i:02d}'
    sa('TownArea/Graveyard/GraveyardTerrain', 'CSGBox3D', name)
    ss(f'TownArea/Graveyard/GraveyardTerrain/{name}', 'size', {'x':1.2,'y':1.8,'z':0.3})
    ss(f'TownArea/Graveyard/GraveyardTerrain/{name}', 'position', {'x':x,'y':0.9,'z':z})
    ss(f'TownArea/Graveyard/GraveyardTerrain/{name}', 'rotation_degrees', {'x':0,'y':i*7,'z':2})
    ss(f'TownArea/Graveyard/GraveyardTerrain/{name}', 'material_override', grave_mat)

# Graveyard fog light
sa('TownArea/Graveyard', 'OmniLight3D', 'GraveyardMist')
ss('TownArea/Graveyard/GraveyardMist', 'position', {'x':30.0,'y':3.0,'z':150.0})
ss('TownArea/Graveyard/GraveyardMist', 'light_color', {'r':0.1,'g':0.15,'b':0.2,'a':1})
ss('TownArea/Graveyard/GraveyardMist', 'light_energy', 0.5)
ss('TownArea/Graveyard/GraveyardMist', 'omni_range', 25.0)

# Dead tree
sa('TownArea/Graveyard/GraveyardTerrain', 'CSGCylinder3D', 'DeadTree')
ss('TownArea/Graveyard/GraveyardTerrain/DeadTree', 'radius', 0.5)
ss('TownArea/Graveyard/GraveyardTerrain/DeadTree', 'height', 7.0)
ss('TownArea/Graveyard/GraveyardTerrain/DeadTree', 'position', {'x':15.0,'y':3.5,'z':150.0})
ss('TownArea/Graveyard/GraveyardTerrain/DeadTree', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.10,'b':0.05,'a':1},
    'roughness':0.95
})

print(f"  Graveyard with {len(grave_positions)} tombstones!")

# ============================================================
# 6. Forest path extending beyond town
# ============================================================
print("\n=== Forest Path Extension ===")

# Path from town to forest
sa('TownArea/Terrain', 'CSGBox3D', 'ForestPath')
ss('TownArea/Terrain/ForestPath', 'size', {'x':5.0,'y':0.1,'z':30.0})
ss('TownArea/Terrain/ForestPath', 'position', {'x':30.0,'y':0.05,'z':150.0})
ss('TownArea/Terrain/ForestPath', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.22,'b':0.16,'a':1},
    'roughness':0.9,
    'uv1_scale':{'x':2,'y':8,'z':2}
})

# Dense forest trees
forest_tree_mat_trunk = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.12,'b':0.06,'a':1},
    'roughness':0.9
}

forest_tree_mat_leaf = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.06,'g':0.18,'b':0.05,'a':1},
    'roughness':0.95
}

import random
random.seed(42)
for i in range(25):
    x = random.uniform(10, 50)
    z = random.uniform(140, 175)
    # Keep path clear
    if abs(x - 30) < 4 and z > 145:
        continue
    
    idx = 31 + i
    sa('TownArea/Terrain', 'CSGCylinder3D', f'ForestTrunk{idx:02d}')
    ss(f'TownArea/Terrain/ForestTrunk{idx:02d}', 'radius', 0.5)
    ss(f'TownArea/Terrain/ForestTrunk{idx:02d}', 'height', 6.0)
    ss(f'TownArea/Terrain/ForestTrunk{idx:02d}', 'position', {'x':x,'y':3.0,'z':z})
    ss(f'TownArea/Terrain/ForestTrunk{idx:02d}', 'material_override', forest_tree_mat_trunk)
    
    sa('TownArea/Terrain', 'CSGSphere3D', f'ForestLeaves{idx:02d}')
    ss(f'TownArea/Terrain/ForestLeaves{idx:02d}', 'radius', 3.5)
    ss(f'TownArea/Terrain/ForestLeaves{idx:02d}', 'position', {'x':x,'y':7.5,'z':z})
    ss(f'TownArea/Terrain/ForestLeaves{idx:02d}', 'material_override', forest_tree_mat_leaf)

print("  Forest path + dense trees added!")

# ============================================================
# 7. Distant castle silhouette
# ============================================================
print("\n=== Distant Castle ===")

castle_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.05,'b':0.08,'a':1},
    'roughness':0.95,'metallic':0.0
}

# Castle on a hill far away
sa('TownArea/Terrain', 'CSGBox3D', 'CastleBase')
ss('TownArea/Terrain/CastleBase', 'size', {'x':20.0,'y':15.0,'z':12.0})
ss('TownArea/Terrain/CastleBase', 'position', {'x':30.0,'y':12.5,'z':180.0})
ss('TownArea/Terrain/CastleBase', 'material_override', castle_mat)

# Castle towers
for tx in [20, 40]:
    sa('TownArea/Terrain', 'CSGCylinder3D', f'CastleTower_{tx}')
    ss(f'TownArea/Terrain/CastleTower_{tx}', 'radius', 3.0)
    ss(f'TownArea/Terrain/CastleTower_{tx}', 'height', 25.0)
    ss(f'TownArea/Terrain/CastleTower_{tx}', 'sides', 12)
    ss(f'TownArea/Terrain/CastleTower_{tx}', 'position', {'x':tx,'y':17.5,'z':180.0})
    ss(f'TownArea/Terrain/CastleTower_{tx}', 'material_override', castle_mat)

# Castle glow (mysterious light)
sa('TownArea/Terrain', 'OmniLight3D', 'CastleGlow')
ss('TownArea/Terrain/CastleGlow', 'position', {'x':30.0,'y':20.0,'z':180.0})
ss('TownArea/Terrain/CastleGlow', 'light_color', {'r':0.3,'g':0.1,'b':0.4,'a':1})
ss('TownArea/Terrain/CastleGlow', 'light_energy', 3.0)
ss('TownArea/Terrain/CastleGlow', 'omni_range', 40.0)

print("  Distant castle silhouette added!")

# ============================================================
# 8. Forest path lights
# ============================================================
print("\n=== Forest Path Lights ===")

for i, z in enumerate(range(145, 175, 5)):
    x = 28 + (i % 2) * 4
    name = f'ForestLight{i:02d}'
    sa('TownArea/Terrain', 'OmniLight3D', name)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':3.0,'z':z})
    ss(f'TownArea/Terrain/{name}', 'light_color', {'r':0.5,'g':0.6,'b':0.3,'a':1})
    ss(f'TownArea/Terrain/{name}', 'light_energy', 0.6)
    ss(f'TownArea/Terrain/{name}', 'omni_range', 6.0)

print("  Forest path lights placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 9 complete!")
