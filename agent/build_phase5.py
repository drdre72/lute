#!/usr/bin/env python3
"""Phase 5: Build the connecting path from Temple exterior to a town hub.
Creates a winding path through a forested area leading to a town gate,
then a small town square with buildings."""
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

# Materials
ground_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.20,'b':0.10,'a':1},
    'roughness':0.95,'metallic':0.0,
    'uv1_scale':{'x':20,'y':20,'z':20}
}

path_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.26,'b':0.20,'a':1},
    'roughness':0.9,'metallic':0.02,
    'uv1_scale':{'x':3,'y':10,'z':3}
}

wood_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.22,'b':0.12,'a':1},
    'roughness':0.8,'metallic':0.0,
    'uv1_scale':{'x':2,'y':2,'z':2}
}

roof_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.10,'a':1},
    'roughness':0.85,'metallic':0.03,
    'uv1_scale':{'x':3,'y':3,'z':3}
}

stone_wall_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.40,'g':0.36,'b':0.30,'a':1},
    'roughness':0.8,'metallic':0.05,
    'uv1_scale':{'x':3,'y':3,'z':3}
}

tree_trunk_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.14,'b':0.08,'a':1},
    'roughness':0.9,'metallic':0.0
}

tree_leaf_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.12,'g':0.25,'b':0.08,'a':1},
    'roughness':0.95,'metallic':0.0
}

# ============================================================
# 1. Ground plane — large terrain patch
# ============================================================
print("=== Ground & Path ===")

add('.', 'Node3D', 'TownArea')
add('TownArea', 'CSGCombiner3D', 'Terrain')

# Large ground patch
add('TownArea/Terrain', 'CSGBox3D', 'GroundPlane')
setprop('TownArea/Terrain/GroundPlane', 'size', {'x':120.0,'y':0.5,'z':120.0})
setprop('TownArea/Terrain/GroundPlane', 'position', {'x':0,'y':-0.25,'z':80.0})
setprop('TownArea/Terrain/GroundPlane', 'use_collision', True)
setprop('TownArea/Terrain/GroundPlane', 'material_override', ground_mat)

# Winding path from temple stairs to town
add('TownArea/Terrain', 'CSGBox3D', 'Path1')
setprop('TownArea/Terrain/Path1', 'size', {'x':6.0,'y':0.1,'z':30.0})
setprop('TownArea/Terrain/Path1', 'position', {'x':0,'y':0.05,'z':50.0})
setprop('TownArea/Terrain/Path1', 'material_override', path_mat)

add('TownArea/Terrain', 'CSGBox3D', 'Path2')
setprop('TownArea/Terrain/Path2', 'size', {'x':30.0,'y':0.1,'z':6.0})
setprop('TownArea/Terrain/Path2', 'position', {'x':15.0,'y':0.05,'z':68.0})
setprop('TownArea/Terrain/Path2', 'material_override', path_mat)

add('TownArea/Terrain', 'CSGBox3D', 'Path3')
setprop('TownArea/Terrain/Path3', 'size', {'x':6.0,'y':0.1,'z':25.0})
setprop('TownArea/Terrain/Path3', 'position', {'x':30.0,'y':0.05,'z':83.0})
setprop('TownArea/Terrain/Path3', 'material_override', path_mat)

print("  Ground & path done!")

# ============================================================
# 2. Trees along the path (simple CSG — trunk + foliage sphere)
# ============================================================
print("\n=== Trees ===")

tree_positions = [
    (-10, 45), (-12, 52), (-8, 58), (-14, 63),
    (10, 48), (14, 55), (12, 62), (16, 68),
    (5, 72), (25, 75), (35, 78), (40, 85),
    (-5, 68), (20, 65), (38, 70),
]

for i, (x, z) in enumerate(tree_positions):
    trunk_name = f'TreeTrunk{i:02d}'
    leaf_name = f'TreeLeaves{i:02d}'
    
    add('TownArea/Terrain', 'CSGCylinder3D', trunk_name)
    setprop(f'TownArea/Terrain/{trunk_name}', 'radius', 0.4)
    setprop(f'TownArea/Terrain/{trunk_name}', 'height', 5.0)
    setprop(f'TownArea/Terrain/{trunk_name}', 'position', {'x':x,'y':2.5,'z':z})
    setprop(f'TownArea/Terrain/{trunk_name}', 'material_override', tree_trunk_mat)
    
    add('TownArea/Terrain', 'CSGSphere3D', leaf_name)
    setprop(f'TownArea/Terrain/{leaf_name}', 'radius', 3.0)
    setprop(f'TownArea/Terrain/{leaf_name}', 'position', {'x':x,'y':6.5,'z':z})
    setprop(f'TownArea/Terrain/{leaf_name}', 'material_override', tree_leaf_mat)

print(f"  {len(tree_positions)} trees placed!")

# ============================================================
# 3. Town gate at end of path
# ============================================================
print("\n=== Town Gate ===")

add('TownArea', 'Node3D', 'TownGate')
add('TownArea/TownGate', 'CSGCombiner3D', 'GateGeometry')

# Gate arch (two pillars + top)
for side, x in [('Left', -5.0), ('Right', 5.0)]:
    name = f'GatePillar_{side}'
    add('TownArea/TownGate/GateGeometry', 'CSGBox3D', name)
    setprop(f'TownArea/TownGate/GateGeometry/{name}', 'size', {'x':2.0,'y':8.0,'z':2.0})
    setprop(f'TownArea/TownGate/GateGeometry/{name}', 'position', {'x':x,'y':4.0,'z':95.0})
    setprop(f'TownArea/TownGate/GateGeometry/{name}', 'material_override', stone_wall_mat)
    setprop(f'TownArea/TownGate/GateGeometry/{name}', 'use_collision', True)

# Gate top
add('TownArea/TownGate/GateGeometry', 'CSGBox3D', 'GateTop')
setprop('TownArea/TownGate/GateGeometry/GateTop', 'size', {'x':12.0,'y':3.0,'z':2.0})
setprop('TownArea/TownGate/GateGeometry/GateTop', 'position', {'x':0,'y':9.5,'z':95.0})
setprop('TownArea/TownGate/GateGeometry/GateTop', 'material_override', stone_wall_mat)

# Gate light
add('TownArea/TownGate', 'OmniLight3D', 'GateLight')
setprop('TownArea/TownGate/GateLight', 'position', {'x':0,'y':7.0,'z':95.0})
setprop('TownArea/TownGate/GateLight', 'light_color', {'r':1.0,'g':0.7,'b':0.3,'a':1})
setprop('TownArea/TownGate/GateLight', 'light_energy', 2.0)
setprop('TownArea/TownGate/GateLight', 'omni_range', 15.0)

print("  Town gate built!")

# ============================================================
# 4. Town square + buildings
# ============================================================
print("\n=== Town Buildings ===")

add('TownArea', 'Node3D', 'TownSquare')
add('TownArea/TownSquare', 'CSGCombiner3D', 'SquareFloor')
add('TownArea/TownSquare/SquareFloor', 'CSGBox3D', 'Cobblestone')
setprop('TownArea/TownSquare/SquareFloor/Cobblestone', 'size', {'x':40.0,'y':0.3,'z':40.0})
setprop('TownArea/TownSquare/SquareFloor/Cobblestone', 'position', {'x':30.0,'y':0.15,'z':110.0})
setprop('TownArea/TownSquare/SquareFloor/Cobblestone', 'use_collision', True)
setprop('TownArea/TownSquare/SquareFloor/Cobblestone', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.28,'g':0.25,'b':0.22,'a':1},
    'roughness':0.85,'metallic':0.05,
    'uv1_scale':{'x':10,'y':10,'z':10}
})

# Central fountain
add('TownArea/TownSquare', 'CSGCylinder3D', 'FountainBase')
setprop('TownArea/TownSquare/FountainBase', 'radius', 4.0)
setprop('TownArea/TownSquare/FountainBase', 'height', 0.8)
setprop('TownArea/TownSquare/FountainBase', 'position', {'x':30.0,'y':0.4,'z':110.0})
setprop('TownArea/TownSquare/FountainBase', 'material_override', stone_wall_mat)

add('TownArea/TownSquare', 'CSGCylinder3D', 'FountainWater')
setprop('TownArea/TownSquare/FountainWater', 'radius', 3.5)
setprop('TownArea/TownSquare/FountainWater', 'height', 0.6)
setprop('TownArea/TownSquare/FountainWater', 'position', {'x':30.0,'y':0.5,'z':110.0})
setprop('TownArea/TownSquare/FountainWater', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.1,'g':0.25,'b':0.4,'a':0.7},
    'roughness':0.1,'metallic':0.0,
    'transparency':2,
    'emission_enabled':True,
    'emission':{'r':0.05,'g':0.15,'b':0.3,'a':1},
    'emission_energy_multiplier':0.5
})

add('TownArea/TownSquare', 'CSGCylinder3D', 'FountainPillar')
setprop('TownArea/TownSquare/FountainPillar', 'radius', 0.5)
setprop('TownArea/TownSquare/FountainPillar', 'height', 3.0)
setprop('TownArea/TownSquare/FountainPillar', 'position', {'x':30.0,'y':1.5,'z':110.0})
setprop('TownArea/TownSquare/FountainPillar', 'material_override', stone_wall_mat)

# Fountain light
add('TownArea/TownSquare', 'OmniLight3D', 'FountainLight')
setprop('TownArea/TownSquare/FountainLight', 'position', {'x':30.0,'y':3.0,'z':110.0})
setprop('TownArea/TownSquare/FountainLight', 'light_color', {'r':0.3,'g':0.5,'b':0.8,'a':1})
setprop('TownArea/TownSquare/FountainLight', 'light_energy', 1.5)
setprop('TownArea/TownSquare/FountainLight', 'omni_range', 12.0)

# Buildings around the square (4 buildings)
buildings = [
    # (name, x, z, rot_y, w, d, h)
    ('Shop', 15.0, 100.0, 0, 8.0, 8.0, 6.0),
    ('Inn', 45.0, 100.0, 0, 10.0, 8.0, 7.0),
    ('House1', 15.0, 120.0, 0, 8.0, 8.0, 5.0),
    ('House2', 45.0, 120.0, 0, 8.0, 8.0, 5.0),
]

for name, x, z, rot, w, d, h in buildings:
    # Walls
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Walls')
    setprop(f'TownArea/TownSquare/{name}_Walls', 'size', {'x':w,'y':h,'z':d})
    setprop(f'TownArea/TownSquare/{name}_Walls', 'position', {'x':x,'y':h/2,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Walls', 'use_collision', True)
    setprop(f'TownArea/TownSquare/{name}_Walls', 'material_override', wood_mat)
    
    # Roof (pyramid-like using a box rotated)
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_Roof')
    setprop(f'TownArea/TownSquare/{name}_Roof', 'size', {'x':w+1.0,'y':0.5,'z':d+1.0})
    setprop(f'TownArea/TownSquare/{name}_Roof', 'position', {'x':x,'y':h+0.25,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Roof', 'material_override', roof_mat)
    
    # Roof peak
    add('TownArea/TownSquare', 'CSGBox3D', f'{name}_RoofPeak')
    setprop(f'TownArea/TownSquare/{name}_RoofPeak', 'size', {'x':w+0.5,'y':2.5,'z':d+0.5})
    setprop(f'TownArea/TownSquare/{name}_RoofPeak', 'position', {'x':x,'y':h+1.5,'z':z})
    setprop(f'TownArea/TownSquare/{name}_RoofPeak', 'material_override', roof_mat)
    
    # Window glow
    add('TownArea/TownSquare', 'OmniLight3D', f'{name}_WindowLight')
    setprop(f'TownArea/TownSquare/{name}_WindowLight', 'position', {'x':x,'y':3.0,'z':z - d/2 - 0.5})
    setprop(f'TownArea/TownSquare/{name}_WindowLight', 'light_color', {'r':1.0,'g':0.8,'b':0.4,'a':1})
    setprop(f'TownArea/TownSquare/{name}_WindowLight', 'light_energy', 1.0)
    setprop(f'TownArea/TownSquare/{name}_WindowLight', 'omni_range', 8.0)

print("  Town buildings built!")

# ============================================================
# 5. Street lamps around town
# ============================================================
print("\n=== Street Lamps ===")

lamp_positions = [
    (22, 105), (38, 105), (22, 115), (38, 115),
    (22, 125), (38, 125),
]

for i, (x, z) in enumerate(lamp_positions):
    name = f'Lamp{i:02d}'
    add('TownArea/TownSquare', 'CSGCylinder3D', f'{name}_Post')
    setprop(f'TownArea/TownSquare/{name}_Post', 'radius', 0.15)
    setprop(f'TownArea/TownSquare/{name}_Post', 'height', 4.0)
    setprop(f'TownArea/TownSquare/{name}_Post', 'position', {'x':x,'y':2.0,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Post', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.15,'g':0.14,'b':0.12,'a':1},
        'roughness':0.4,'metallic':0.6
    })
    
    add('TownArea/TownSquare', 'OmniLight3D', f'{name}_Light')
    setprop(f'TownArea/TownSquare/{name}_Light', 'position', {'x':x,'y':4.2,'z':z})
    setprop(f'TownArea/TownSquare/{name}_Light', 'light_color', {'r':1.0,'g':0.85,'b':0.5,'a':1})
    setprop(f'TownArea/TownSquare/{name}_Light', 'light_energy', 1.5)
    setprop(f'TownArea/TownSquare/{name}_Light', 'omni_range', 10.0)

print(f"  {len(lamp_positions)} street lamps placed!")

# ============================================================
# 6. Town camera
# ============================================================
print("\n=== Town Camera ===")

add('TownArea', 'Camera3D', 'TownCamera')
setprop('TownArea/TownCamera', 'position', {'x':30.0,'y':15.0,'z':130.0})
setprop('TownArea/TownCamera', 'rotation_degrees', {'x':-15,'y':0,'z':0})
setprop('TownArea/TownCamera', 'fov', 70)

print("  Town camera set!")

# ============================================================
# 7. Extend environment fog to cover town area
# ============================================================
print("\n=== Environment Update ===")

setprop('WorldEnvironment', 'environment', {
    'class':'Environment',
    'background_mode':3,
    'sky':{'class':'Sky','sky_material':{'class':'GradientSky','top_color':{'r':0.02,'g':0.02,'b':0.08,'a':1},'bottom_color':{'r':0.15,'g':0.10,'b':0.18,'a':1},'curve':0.8}},
    'ambient_light_source':2,
    'ambient_light_color':{'r':0.12,'g':0.12,'b':0.18,'a':1},
    'ambient_light_energy':0.5,
    'fog_enabled':True,
    'fog_light_color':{'r':0.06,'g':0.06,'b':0.14,'a':1},
    'fog_light_energy':0.6,
    'fog_density':0.04,
    'fog_aerial_perspective':0.6,
    'glow_enabled':True,
    'glow_intensity':1.2,
    'glow_strength':1.1,
    'glow_blend_mode':1,
    'glow_bloom':0.2,
    'volumetric_fog_enabled':True,
    'volumetric_fog_density':0.01,
    'volumetric_fog_albedo':{'r':0.1,'g':0.1,'b':0.2,'a':1},
    'volumetric_fog_emission':{'r':0.04,'g':0.06,'b':0.12,'a':1},
    'volumetric_fog_emission_energy':0.2,
    'tonemap_mode':2,
    'tonemap_white':1.2,
    'ssao_enabled':True,
    'ssao_radius':1.0,
    'ssao_intensity':1.5,
    'ssao_power':1.5
})

print("  Environment updated!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")

r = call_tool('get_scene_tree', {})
if r.get('ok'):
    tree = r['tree']
    print(f"\nRoot: {tree['name']} ({tree['type']})")
    print(f"Top-level: {len(tree.get('children', []))} nodes")
    for child in tree.get('children', []):
        n = len(child.get('children', []))
        print(f"  - {child['name']} ({child['type']}) [{n} children]")
        if n > 0 and child['name'] in ('Architecture', 'TownArea'):
            for sub in child.get('children', []):
                sn = len(sub.get('children', []))
                print(f"      - {sub['name']} ({sub['type']}) [{sn} children]")

print("\nPhase 5 complete!")
