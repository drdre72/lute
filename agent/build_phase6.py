#!/usr/bin/env python3
"""Phase 6: Town detail pass — window/door cutouts on buildings, chimneys,
market stalls, clock tower, stone walls around town, more vegetation."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    print(f"  add {name}: {r.get('ok')}")
    return r

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    print(f"  set {path.split('/')[-1]}.{prop}: {r.get('ok')}")
    return r

# ============================================================
# 1. Window & door cutouts on existing buildings
# ============================================================
print("=== Building Cutouts ===")

buildings = [
    ('Shop', 15.0, 100.0, 8.0, 8.0, 6.0),
    ('Inn', 45.0, 100.0, 10.0, 8.0, 7.0),
    ('House1', 15.0, 120.0, 8.0, 8.0, 5.0),
    ('House2', 45.0, 120.0, 8.0, 8.0, 5.0),
]

for name, x, z, w, d, h in buildings:
    walls_path = f'TownArea/TownSquare/{name}_Walls'
    
    # Door cutout
    add(walls_path, 'CSGBox3D', f'{name}_DoorCut')
    setprop(f'{walls_path}/{name}_DoorCut', 'operation', 2)
    setprop(f'{walls_path}/{name}_DoorCut', 'size', {'x':2.0,'y':3.5,'z':1.0})
    setprop(f'{walls_path}/{name}_DoorCut', 'position', {'x':0,'y':1.75,'z':d/2})
    
    # Window cutouts (2 windows on front face)
    for wi, wx in enumerate([-2.0, 2.0]):
        add(walls_path, 'CSGBox3D', f'{name}_WinCut{wi}')
        setprop(f'{walls_path}/{name}_WinCut{wi}', 'operation', 2)
        setprop(f'{walls_path}/{name}_WinCut{wi}', 'size', {'x':1.5,'y':1.5,'z':1.0})
        setprop(f'{walls_path}/{name}_WinCut{wi}', 'position', {'x':wx,'y':3.5,'z':d/2})

print("  Building cutouts done!")

# ============================================================
# 2. Chimneys with warm glow
# ============================================================
print("\n=== Chimneys ===")

chimney_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.25,'b':0.20,'a':1},
    'roughness':0.85,'metallic':0.05
}

for name, x, z, w, d, h in buildings:
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Chimney')
    setprop(f'TownArea/TownSquare/{name}_Chimney', 'size', {'x':1.0,'y':3.0,'z':1.0})
    setprop(f'TownArea/TownSquare/{name}_Chimney', 'position', {'x':x + w/2 - 1.5,'y':h + 2.0,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Chimney', 'material_override', chimney_mat)

print("  Chimneys added!")

# ============================================================
# 3. Clock tower at town center
# ============================================================
print("\n=== Clock Tower ===")

add('TownArea/TownSquare', 'CSGBox3D', 'ClockTower_Base')
setprop('TownArea/TownSquare/ClockTower_Base', 'size', {'x':5.0,'y':15.0,'z':5.0})
setprop('TownArea/TownSquare/ClockTower_Base', 'position', {'x':30.0,'y':7.5,'z':95.0})
setprop('TownArea/TownSquare/ClockTower_Base', 'use_collision', True)
setprop('TownArea/TownSquare/ClockTower_Base', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.32,'b':0.28,'a':1},
    'roughness':0.7,'metallic':0.1,
    'uv1_scale':{'x':2,'y':6,'z':2}
})

# Tower roof
add('TownArea/TownSquare', 'CSGCylinder3D', 'ClockTower_Roof')
setprop('TownArea/TownSquare/ClockTower_Roof', 'radius', 3.5)
setprop('TownArea/TownSquare/ClockTower_Roof', 'height', 4.0)
setprop('TownArea/TownSquare/ClockTower_Roof', 'sides', 4)
setprop('TownArea/TownSquare/ClockTower_Roof', 'position', {'x':30.0,'y':17.0,'z':95.0})
setprop('TownArea/TownSquare/ClockTower_Roof', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.12,'b':0.08,'a':1},
    'roughness':0.8,'metallic':0.05
})

# Clock face (emissive)
add('TownArea/TownSquare', 'CSGCylinder3D', 'ClockFace')
setprop('TownArea/TownSquare/ClockFace', 'radius', 1.5)
setprop('TownArea/TownSquare/ClockFace', 'height', 0.2)
setprop('TownArea/TownSquare/ClockFace', 'position', {'x':30.0,'y':11.0,'z':97.6})
setprop('TownArea/TownSquare/ClockFace', 'rotation_degrees', {'x':90,'y':0,'z':0})
setprop('TownArea/TownSquare/ClockFace', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.9,'g':0.85,'b':0.6,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.5,'g':0.4,'b':0.2,'a':1},
    'emission_energy_multiplier':1.5,
    'roughness':0.3
})

# Tower light
add('TownArea/TownSquare', 'OmniLight3D', 'ClockTowerLight')
setprop('TownArea/TownSquare/ClockTowerLight', 'position', {'x':30.0,'y':12.0,'z':95.0})
setprop('TownArea/TownSquare/ClockTowerLight', 'light_color', {'r':0.8,'g':0.6,'b':0.3,'a':1})
setprop('TownArea/TownSquare/ClockTowerLight', 'light_energy', 2.0)
setprop('TownArea/TownSquare/ClockTowerLight', 'omni_range', 20.0)

print("  Clock tower built!")

# ============================================================
# 4. Market stalls in the square
# ============================================================
print("\n=== Market Stalls ===")

stall_canvas_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.5,'g':0.3,'b':0.15,'a':1},
    'roughness':0.7,'metallic':0.0
}

stall_positions = [
    (24, 107), (28, 107), (32, 107), (36, 107),
]

for i, (x, z) in enumerate(stall_positions):
    name = f'Stall{i}'
    # Table
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Table')
    setprop(f'TownArea/TownSquare/{name}_Table', 'size', {'x':3.0,'y':0.2,'z':2.0})
    setprop(f'TownArea/TownSquare/{name}_Table', 'position', {'x':x,'y':1.5,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Table', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.30,'g':0.20,'b':0.10,'a':1},
        'roughness':0.8
    })
    
    # Table legs
    for lx in [-1.3, 1.3]:
        for lz in [-0.8, 0.8]:
            leg_name = f'{name}_Leg_{lx}_{lz}'
            add('TownArea/TownSquare', 'CSGBox3D', leg_name)
            setprop(f'TownArea/TownSquare/{leg_name}', 'size', {'x':0.15,'y':1.4,'z':0.15})
            setprop(f'TownArea/TownSquare/{leg_name}', 'position', {'x':x+lx,'y':0.7,'z':z+lz})
    
    # Canvas top
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Canvas')
    setprop(f'TownArea/TownSquare/{name}_Canvas', 'size', {'x':3.5,'y':0.1,'z':2.5})
    setprop(f'TownArea/TownSquare/{name}_Canvas', 'position', {'x':x,'y':3.0,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Canvas', 'material_override', stall_canvas_mat)
    
    # Canvas supports
    for sx in [-1.5, 1.5]:
        add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Pole_{sx}')
        setprop(f'TownArea/TownSquare/{name}_Pole_{sx}', 'size', {'x':0.1,'y':3.0,'z':0.1})
        setprop(f'TownArea/TownSquare/{name}_Pole_{sx}', 'position', {'x':x+sx,'y':1.5,'z':z})

print(f"  {len(stall_positions)} market stalls built!")

# ============================================================
# 5. Town perimeter wall
# ============================================================
print("\n=== Town Walls ===")

wall_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.32,'b':0.28,'a':1},
    'roughness':0.8,'metallic':0.05,
    'uv1_scale':{'x':5,'y':2,'z':5}
}

# Back wall
add('TownArea/TownSquare', 'CSGBox3D', 'TownWall_Back')
setprop('TownArea/TownSquare/TownWall_Back', 'size', {'x':50.0,'y':6.0,'z':1.0})
setprop('TownArea/TownSquare/TownWall_Back', 'position', {'x':30.0,'y':3.0,'z':132.0})
setprop('TownArea/TownSquare/TownWall_Back', 'use_collision', True)
setprop('TownArea/TownSquare/TownWall_Back', 'material_override', wall_mat)

# Left wall
add('TownArea/TownSquare', 'CSGBox3D', 'TownWall_Left')
setprop('TownArea/TownSquare/TownWall_Left', 'size', {'x':1.0,'y':6.0,'z':40.0})
setprop('TownArea/TownSquare/TownWall_Left', 'position', {'x':8.0,'y':3.0,'z':110.0})
setprop('TownArea/TownSquare/TownWall_Left', 'use_collision', True)
setprop('TownArea/TownSquare/TownWall_Left', 'material_override', wall_mat)

# Right wall
add('TownArea/TownSquare', 'CSGBox3D', 'TownWall_Right')
setprop('TownArea/TownSquare/TownWall_Right', 'size', {'x':1.0,'y':6.0,'z':40.0})
setprop('TownArea/TownSquare/TownWall_Right', 'position', {'x':52.0,'y':3.0,'z':110.0})
setprop('TownArea/TownSquare/TownWall_Right', 'use_collision', True)
setprop('TownArea/TownSquare/TownWall_Right', 'material_override', wall_mat)

# Wall towers (corner towers)
for tx, tz in [(8, 132), (52, 132)]:
    name = f'WallTower_{tx}_{tz}'
    add('TownArea/TownSquare', 'CSGBox3D', name)
    setprop(f'TownArea/TownSquare/{name}', 'size', {'x':3.0,'y':9.0,'z':3.0})
    setprop(f'TownArea/TownSquare/{name}', 'position', {'x':tx,'y':4.5,'z':tz})
    setprop(f'TownArea/TownSquare/{name}', 'material_override', wall_mat)
    setprop(f'TownArea/TownSquare/{name}', 'use_collision', True)

print("  Town walls built!")

# ============================================================
# 6. More vegetation — bushes and rocks
# ============================================================
print("\n=== Vegetation Details ===")

bush_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.20,'b':0.06,'a':1},
    'roughness':0.95,'metallic':0.0
}

rock_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.28,'b':0.25,'a':1},
    'roughness':0.9,'metallic':0.03
}

# Bushes scattered around
bush_positions = [
    (5, 48), (8, 55), (-5, 60), (18, 72),
    (25, 65), (35, 70), (42, 80), (48, 88),
    (12, 105), (48, 105), (12, 125), (48, 125),
]

for i, (x, z) in enumerate(bush_positions):
    name = f'Bush{i:02d}'
    add('TownArea/Terrain', 'CSGSphere3D', name)
    setprop(f'TownArea/Terrain/{name}', 'radius', 0.8)
    setprop(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.5,'z':z})
    setprop(f'TownArea/Terrain/{name}', 'material_override', bush_mat)

# Rocks
rock_positions = [
    (-3, 50), (7, 58), (-8, 65), (20, 70), (33, 75),
]

for i, (x, z) in enumerate(rock_positions):
    name = f'Rock{i:02d}'
    add('TownArea/Terrain', 'CSGBox3D', name)
    setprop(f'TownArea/Terrain/{name}', 'size', {'x':1.5,'y':1.0,'z':1.2})
    setprop(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.5,'z':z})
    setprop(f'TownArea/Terrain/{name}', 'material_override', rock_mat)
    setprop(f'TownArea/Terrain/{name}', 'rotation_degrees', {'x':0,'y':i*30,'z':5})

print(f"  {len(bush_positions)} bushes, {len(rock_positions)} rocks placed!")

# ============================================================
# 7. Path lights along the winding path
# ============================================================
print("\n=== Path Lights ===")

path_light_positions = [
    (-3, 42), (3, 48), (-3, 55), (3, 62),
    (8, 68), (20, 72), (28, 78), (33, 82),
]

for i, (x, z) in enumerate(path_light_positions):
    name = f'PathLight{i:02d}'
    add('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Post')
    setprop(f'TownArea/Terrain/{name}_Post', 'radius', 0.1)
    setprop(f'TownArea/Terrain/{name}_Post', 'height', 3.0)
    setprop(f'TownArea/Terrain/{name}_Post', 'position', {'x':x,'y':1.5,'z':z})
    setprop(f'TownArea/Terrain/{name}_Post', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.12,'g':0.10,'b':0.08,'a':1},
        'roughness':0.4,'metallic':0.5
    })
    
    add('TownArea/Terrain', 'OmniLight3D', f'{name}_Glow')
    setprop(f'TownArea/Terrain/{name}_Glow', 'position', {'x':x,'y':3.2,'z':z})
    setprop(f'TownArea/Terrain/{name}_Glow', 'light_color', {'r':0.9,'g':0.7,'b':0.3,'a':1})
    setprop(f'TownArea/Terrain/{name}_Glow', 'light_energy', 1.0)
    setprop(f'TownArea/Terrain/{name}_Glow', 'omni_range', 8.0)

print(f"  {len(path_light_positions)} path lights placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 6 complete!")
