#!/usr/bin/env python3
"""Batch builder: finishes Zone D + adds Zone A (Exterior) and Zone B (Vestibule)
to the main_nave scene, then applies materials and atmospheric lighting."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    print(f"  add {name}: {r}")
    return r

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    print(f"  set {path}.{prop}: {r}")
    return r

# ============================================================
# PHASE 1: Finish Zone D (items 23-51 from MetaList_ZoneD.txt)
# ============================================================
print("=== PHASE 1: Finish Zone D ===")

# Step05
add('Architecture/NaveCombiner/AltarPlatform', 'CSGBox3D', 'Step05')
setprop('Architecture/NaveCombiner/AltarPlatform/Step05', 'size', {'x':10.0,'y':0.2,'z':0.4})
setprop('Architecture/NaveCombiner/AltarPlatform/Step05', 'position', {'x':0,'y':0.3,'z':-2.8})

# Step06
add('Architecture/NaveCombiner/AltarPlatform', 'CSGBox3D', 'Step06')
setprop('Architecture/NaveCombiner/AltarPlatform/Step06', 'size', {'x':10.0,'y':0.2,'z':0.4})
setprop('Architecture/NaveCombiner/AltarPlatform/Step06', 'position', {'x':0,'y':0.5,'z':-3.2})

# Door of Time Slab
add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorOfTimeSlab')
setprop('Architecture/NaveCombiner/DoorOfTimeSlab', 'size', {'x':7.0,'y':10.0,'z':0.6})
setprop('Architecture/NaveCombiner/DoorOfTimeSlab', 'position', {'x':0,'y':6.2,'z':22.3})
setprop('Architecture/NaveCombiner/DoorOfTimeSlab', 'use_collision', True)
setprop('Architecture/NaveCombiner/DoorOfTimeSlab', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.23,'b':0.21,'a':1},
    'roughness':0.75,'metallic':0.1
})

# Pedestal Table
add('Architecture/NaveCombiner/AltarPlatform', 'CSGBox3D', 'PedestalTable')
setprop('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'size', {'x':3.0,'y':1.1,'z':1.2})
setprop('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'position', {'x':0,'y':1.15,'z':0.6})
setprop('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'use_collision', True)
setprop('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.33,'b':0.3,'a':1},
    'roughness':0.6,'metallic':0.2
})

# Sockets (cutouts)
for name, x in [('SocketLeft', -0.8), ('SocketCenter', 0), ('SocketRight', 0.8)]:
    path = f'Architecture/NaveCombiner/AltarPlatform/PedestalTable/{name}'
    add('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'CSGBox3D', name)
    setprop(path, 'operation', 2)
    setprop(path, 'size', {'x':0.3,'y':0.2,'z':0.3})
    setprop(path, 'position', {'x':x,'y':0.35,'z':0})

# Apply stone material to steps and platform
stone_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.32,'g':0.30,'b':0.27,'a':1},
    'roughness':0.8,'metallic':0.05,
    'uv1_scale':{'x':2,'y':1,'z':2}
}
setprop('Architecture/NaveCombiner/AltarPlatform', 'material_override', stone_mat)
for i in range(1,7):
    setprop(f'Architecture/NaveCombiner/AltarPlatform/Step0{i}', 'material_override', stone_mat)

print("  Zone D complete!")

# ============================================================
# PHASE 2: Zone A - Exterior Facade & Entrance Stairs
# ============================================================
print("\n=== PHASE 2: Zone A - Exterior Facade ===")

# Exterior group
add('Architecture', 'Node3D', 'Exterior')
add('Architecture/Exterior', 'CSGCombiner3D', 'ExteriorPlaza')

# Plaza floor
add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', 'PlazaFloor')
setprop('Architecture/Exterior/ExteriorPlaza/PlazaFloor', 'size', {'x':24.0,'y':0.5,'z':12.0})
setprop('Architecture/Exterior/ExteriorPlaza/PlazaFloor', 'position', {'x':0,'y':-0.25,'z':24.0})
setprop('Architecture/Exterior/ExteriorPlaza/PlazaFloor', 'use_collision', True)
setprop('Architecture/Exterior/ExteriorPlaza/PlazaFloor', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.28,'g':0.26,'b':0.24,'a':1},
    'roughness':0.9,'metallic':0.03,
    'uv1_scale':{'x':6,'y':6,'z':6}
})

# Entrance stairs (12 steps, 0.2m rise each, 0.6m tread, 18m wide)
for i in range(12):
    name = f'PlazaStep{i:02d}'
    y = -0.25 + i * 0.2
    z = 30.0 + i * 0.6
    add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', name)
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'size', {'x':18.0,'y':0.2,'z':0.6})
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':0,'y':y,'z':z})
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', stone_mat)

# Exterior facade pillars (2 pillars flanking entrance)
for side, x in [('Left', -8.0), ('Right', 8.0)]:
    name = f'FacadePillar_{side}'
    add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', name)
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'size', {'x':2.0,'y':12.0,'z':2.0})
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':x,'y':6.0,'z':22.0})
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', stone_mat)

# Front wall (connects to nave entrance, with opening)
add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', 'FrontWall')
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall', 'size', {'x':22.0,'y':20.0,'z':1.0})
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall', 'position', {'x':0,'y':10.0,'z':20.5})
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall', 'use_collision', True)
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall', 'material_override', stone_mat)

# Cut entrance opening in front wall
add('Architecture/Exterior/ExteriorPlaza/FrontWall', 'CSGBox3D', 'EntranceCut')
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall/EntranceCut', 'operation', 2)
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall/EntranceCut', 'size', {'x':6.0,'y':8.0,'z':2.0})
setprop('Architecture/Exterior/ExteriorPlaza/FrontWall/EntranceCut', 'position', {'x':0,'y':4.0,'z':0})

# Archway top (above entrance)
add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', 'ArchwayTop')
setprop('Architecture/Exterior/ExteriorPlaza/ArchwayTop', 'size', {'x':8.0,'y':4.0,'z':1.0})
setprop('Architecture/Exterior/ExteriorPlaza/ArchwayTop', 'position', {'x':0,'y':10.0,'z':20.5})
setprop('Architecture/Exterior/ExteriorPlaza/ArchwayTop', 'material_override', stone_mat)

print("  Zone A complete!")

# ============================================================
# PHASE 3: Zone B - Vestibule / Entry Hallway
# ============================================================
print("\n=== PHASE 3: Zone B - Vestibule ===")

add('Architecture', 'Node3D', 'Vestibule')
add('Architecture/Vestibule', 'CSGCombiner3D', 'VestibuleGeometry')

# Vestibule floor
add('Architecture/Vestibule/VestibuleGeometry', 'CSGBox3D', 'VestFloor')
setprop('Architecture/Vestibule/VestibuleGeometry/VestFloor', 'size', {'x':8.0,'y':0.5,'z':10.0})
setprop('Architecture/Vestibule/VestibuleGeometry/VestFloor', 'position', {'x':0,'y':-0.25,'z':25.0})
setprop('Architecture/Vestibule/VestibuleGeometry/VestFloor', 'use_collision', True)
setprop('Architecture/Vestibule/VestibuleGeometry/VestFloor', 'material_override', stone_mat)

# Vestibule walls
for side, x in [('Left', -4.0), ('Right', 4.0)]:
    name = f'VestWall_{side}'
    add('Architecture/Vestibule/VestibuleGeometry', 'CSGBox3D', name)
    setprop(f'Architecture/Vestibule/VestibuleGeometry/{name}', 'size', {'x':0.5,'y':10.0,'z':10.0})
    setprop(f'Architecture/Vestibule/VestibuleGeometry/{name}', 'position', {'x':x,'y':5.0,'z':25.0})
    setprop(f'Architecture/Vestibule/VestibuleGeometry/{name}', 'use_collision', True)
    setprop(f'Architecture/Vestibule/VestibuleGeometry/{name}', 'material_override', stone_mat)

# Vestibule ceiling (vaulted)
add('Architecture/Vestibule/VestibuleGeometry', 'CSGCylinder3D', 'VestVault')
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'radius', 4.0)
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'sides', 16)
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'height', 10.0)
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'rotation_degrees', {'x':90,'y':0,'z':0})
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'position', {'x':0,'y':5.0,'z':25.0})
setprop('Architecture/Vestibule/VestibuleGeometry/VestVault', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.36,'g':0.34,'b':0.30,'a':1},
    'roughness':0.85,'metallic':0.03,
    'uv1_scale':{'x':2,'y':4,'z':2}
})

print("  Zone B complete!")

# ============================================================
# PHASE 4: Atmospheric Lighting & Post-Processing
# ============================================================
print("\n=== PHASE 4: Atmosphere ===")

# Update WorldEnvironment with fog and better ambient
setprop('WorldEnvironment', 'environment', {
    'class':'Environment',
    'background_mode':1,
    'background_color':{'r':0.02,'g':0.02,'b':0.05,'a':1},
    'ambient_light_source':2,
    'ambient_light_color':{'r':0.12,'g':0.12,'b':0.18,'a':1},
    'ambient_light_energy':0.4,
    'fog_enabled':True,
    'fog_light_color':{'r':0.05,'g':0.05,'b':0.12,'a':1},
    'fog_light_energy':0.5,
    'fog_density':0.08,
    'fog_aerial_perspective':0.5,
    'glow_enabled':True,
    'glow_intensity':0.8,
    'glow_strength':0.9,
    'glow_blend_mode':1,
    'volumetric_fog_enabled':True,
    'volumetric_fog_density':0.02,
    'volumetric_fog_albedo':{'r':0.1,'g':0.1,'b':0.2,'a':1},
    'volumetric_fog_emission':{'r':0.05,'g':0.08,'b':0.15,'a':1},
    'volumetric_fog_emission_energy':0.3
})

# Altar glow light (warm light from Door of Time)
add('.', 'OmniLight3D', 'AltarGlow')
setprop('AltarGlow', 'position', {'x':0,'y':6.0,'z':21.0})
setprop('AltarGlow', 'light_color', {'r':1.0,'g':0.85,'b':0.6,'a':1})
setprop('AltarGlow', 'light_energy', 3.0)
setprop('AltarGlow', 'omni_range', 25.0)
setprop('AltarGlow', 'omni_attenuation', 1.5)

# Entrance light
add('.', 'OmniLight3D', 'EntranceLight')
setprop('EntranceLight', 'position', {'x':0,'y':5.0,'z':20.0})
setprop('EntranceLight', 'light_color', {'r':0.9,'g':0.85,'b':0.7,'a':1})
setprop('EntranceLight', 'light_energy', 1.5)
setprop('EntranceLight', 'omni_range', 20.0)

# Nave accent lights (warm pools between pillars)
for z in [12, 0, -12]:
    add('.', 'OmniLight3D', f'NaveAccent_z{z}')
    setprop(f'NaveAccent_z{z}', 'position', {'x':0,'y':8.0,'z':z})
    setprop(f'NaveAccent_z{z}', 'light_color', {'r':0.8,'g':0.7,'b':0.5,'a':1})
    setprop(f'NaveAccent_z{z}', 'light_energy', 0.8)
    setprop(f'NaveAccent_z{z}', 'omni_range', 15.0)

# Camera for demo
add('.', 'Camera3D', 'DemoCamera')
setprop('DemoCamera', 'position', {'x':0,'y':3.0,'z':30.0})
setprop('DemoCamera', 'rotation_degrees', {'x':-5,'y':180,'z':0})
setprop('DemoCamera', 'fov', 75)

print("  Atmosphere complete!")

# ============================================================
# PHASE 5: Save
# ============================================================
print("\n=== Saving scene ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")

# Verify
r = call_tool('get_scene_tree', {})
if r.get('ok'):
    tree = r['tree']
    print(f"Root: {tree['name']} ({tree['type']})")
    print(f"Children: {len(tree.get('children', []))}")
    for child in tree.get('children', []):
        print(f"  - {child['name']} ({child['type']}) [{len(child.get('children', []))} children]")

print("\nDone!")
