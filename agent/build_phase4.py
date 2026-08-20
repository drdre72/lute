#!/usr/bin/env python3
"""Phase 4: High-fidelity detail pass — ceiling ribs, arches between pillars,
procedural noise textures, refined lighting, better camera."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

def safe_add(parent, type, name):
    r = add(parent, type, name)
    print(f"  add {name}: {r.get('ok')}")
    return r

def safe_set(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  set {path.split('/')[-1]}.{prop}: {r.get('ok')}")
    return r

# ============================================================
# 1. Ceiling ribs — transverse arches between pillars
# ============================================================
print("=== Ceiling Ribs (Transverse Arches) ===")

rib_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.38,'g':0.35,'b':0.30,'a':1},
    'roughness':0.7,'metallic':0.08,
    'uv1_scale':{'x':1,'y':1,'z':1}
}

# 5 transverse arches at z = 12, 6, 0, -6, -12 (between pillar pairs)
for i, z in enumerate([12, 6, 0, -6, -12]):
    name = f'RibArch{i}'
    safe_add('Architecture/NaveCombiner', 'CSGBox3D', name)
    safe_set(f'Architecture/NaveCombiner/{name}', 'size', {'x':22.0,'y':0.6,'z':0.8})
    safe_set(f'Architecture/NaveCombiner/{name}', 'position', {'x':0,'y':14.5,'z':z})
    safe_set(f'Architecture/NaveCombiner/{name}', 'material_override', rib_mat)

# Longitudinal ribs (running along the vault ceiling, 3 lines)
for i, x in enumerate([-7, 0, 7]):
    name = f'LongRib{i}'
    safe_add('Architecture/NaveCombiner', 'CSGBox3D', name)
    safe_set(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.5,'y':0.5,'z':40.0})
    safe_set(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':14.5,'z':0})
    safe_set(f'Architecture/NaveCombiner/{name}', 'material_override', rib_mat)

print("  Ceiling ribs complete!")

# ============================================================
# 2. Pillar bases (wider footings for visual weight)
# ============================================================
print("\n=== Pillar Bases ===")

base_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.28,'b':0.25,'a':1},
    'roughness':0.85,'metallic':0.05,
    'uv1_scale':{'x':1,'y':1,'z':1}
}

for side in ['L', 'R']:
    for i in range(6):
        name = f'Base_{side}{i}'
        pillar = f'Architecture/NaveCombiner/Pillar_{side}{i}'
        safe_add(pillar, 'CSGBox3D', name)
        safe_set(f'{pillar}/{name}', 'size', {'x':2.6,'y':0.8,'z':2.6})
        safe_set(f'{pillar}/{name}', 'position', {'x':0,'y':-3.9,'z':0})
        safe_set(f'{pillar}/{name}', 'material_override', base_mat)

print("  Pillar bases complete!")

# ============================================================
# 3. Refined materials with more depth
# ============================================================
print("\n=== Refined Material Pass ===")

# Floor — darker, more polished stone
safe_set('Architecture/NaveCombiner/Floor', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.16,'b':0.14,'a':1},
    'roughness':0.65,'metallic':0.15,
    'uv1_scale':{'x':8,'y':8,'z':8}
})

# Nave vault — warmer with more variation
safe_set('Architecture/NaveCombiner/NaveVault', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.44,'g':0.40,'b':0.34,'a':1},
    'roughness':0.78,'metallic':0.06,
    'uv1_scale':{'x':6,'y':16,'z':6}
})

# Rear facade
safe_set('Architecture/NaveCombiner/RearFacade', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.32,'g':0.29,'b':0.26,'a':1},
    'roughness':0.82,'metallic':0.05,
    'uv1_scale':{'x':5,'y':5,'z':1}
})

# Rose window — more vibrant
safe_set('Architecture/NaveCombiner/RearFacade/RoseWindowCut', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.08,'b':0.20,'a':0.7},
    'emission_enabled':True,
    'emission':{'r':0.2,'g':0.45,'b':0.9,'a':1},
    'emission_energy_multiplier':3.5,
    'roughness':0.15,'metallic':0.0,
    'transparency':2
})

# Pillars — slightly different stone for visual variety
pillar_stone = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.33,'g':0.31,'b':0.28,'a':1},
    'roughness':0.55,'metallic':0.18,
    'uv1_scale':{'x':1,'y':6,'z':1}
}
for side in ['L', 'R']:
    for i in range(6):
        safe_set(f'Architecture/NaveCombiner/Pillar_{side}{i}', 'material_override', pillar_stone)

# Door of Time slab — darker, more mysterious
safe_set('Architecture/NaveCombiner/DoorOfTimeSlab', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.12,'g':0.10,'b':0.09,'a':1},
    'roughness':0.4,'metallic':0.3,
    'emission_enabled':True,
    'emission':{'r':0.05,'g':0.03,'b':0.02,'a':1},
    'emission_energy_multiplier':0.3
})

# Altar platform — polished stone
safe_set('Architecture/NaveCombiner/AltarPlatform', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.23,'b':0.20,'a':1},
    'roughness':0.45,'metallic':0.2,
    'uv1_scale':{'x':3,'y':1,'z':3}
})

# Floor inlay — gold-ish metallic
safe_set('Architecture/NaveCombiner/FloorInlay', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.28,'b':0.12,'a':1},
    'roughness':0.3,'metallic':0.6,
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.07,'b':0.02,'a':1},
    'emission_energy_multiplier':0.5,
    'uv1_scale':{'x':1,'y':12,'z':1}
})

# Altar inlay — matching gold
safe_set('Architecture/NaveCombiner/AltarInlay', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.28,'b':0.12,'a':1},
    'roughness':0.3,'metallic':0.6,
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.07,'b':0.02,'a':1},
    'emission_energy_multiplier':0.5
})

print("  Materials refined!")

# ============================================================
# 4. Lighting tuning — more dramatic, warmer
# ============================================================
print("\n=== Lighting Tune ===")

# Main directional light — softer, from above
safe_set('NaveLight', 'light_energy', 0.8)
safe_set('NaveLight', 'light_color', {'r':0.6,'g':0.55,'b':0.45,'a':1})
safe_set('NaveLight', 'rotation_degrees', {'x':-40,'y':30,'z':0})

# Rose window glow — brighter blue
safe_set('RoseWindowGlow', 'light_energy', 3.5)
safe_set('RoseWindowGlow', 'position', {'x':0,'y':13,'z':-17})

# Altar glow — warmer, stronger
safe_set('AltarGlow', 'light_energy', 4.0)
safe_set('AltarGlow', 'light_color', {'r':1.0,'g':0.75,'b':0.4,'a':1})
safe_set('AltarGlow', 'omni_range', 30.0)

# Nave accents — warmer pools
for z in [12, 0, -12]:
    safe_set(f'NaveAccent_z{z}', 'light_energy', 1.2)
    safe_set(f'NaveAccent_z{z}', 'light_color', {'r':0.9,'g':0.75,'b':0.5,'a':1})
    safe_set(f'NaveAccent_z{z}', 'omni_range', 18.0)

# Torch lights — flicker-like warm orange
for side in ['Left', 'Right']:
    safe_set(f'TorchLight_{side}', 'light_energy', 2.5)
    safe_set(f'TorchLight_{side}', 'light_color', {'r':1.0,'g':0.5,'b':0.15,'a':1})
    safe_set(f'TorchLight_{side}', 'omni_range', 15.0)

# Window lights — more visible blue glow
for key in ['L0','R0','L1','R1','L2','R2']:
    safe_set(f'WinLight_{key}', 'light_energy', 0.8)
    safe_set(f'WinLight_{key}', 'light_color', {'r':0.15,'g':0.25,'b':0.6,'a':1})

print("  Lighting tuned!")

# ============================================================
# 5. Better DemoCamera — hero shot from altar looking toward rose window
# ============================================================
print("\n=== Camera Update ===")

# Main hero camera — from altar area looking down the nave
safe_set('DemoCamera', 'position', {'x':3,'y':4.5,'z':18})
safe_set('DemoCamera', 'rotation_degrees', {'x':-3,'y':180,'z':0})
safe_set('DemoCamera', 'fov', 70)

# Second camera — from entrance looking toward altar
safe_add('.', 'Camera3D', 'AltarCamera')
safe_set('AltarCamera', 'position', {'x':0,'y':4.0,'z':28})
safe_set('AltarCamera', 'rotation_degrees', {'x':-2,'y':0,'z':0})
safe_set('AltarCamera', 'fov', 65)

# Third camera — close-up of Door of Time
safe_add('.', 'Camera3D', 'DoorCamera')
safe_set('DoorCamera', 'position', {'x':0,'y':6.0,'z':19})
safe_set('DoorCamera', 'rotation_degrees', {'x':0,'y':0,'z':0})
safe_set('DoorCamera', 'fov', 50)

print("  Cameras set!")

# ============================================================
# 6. Decorative trim — base moldings along walls
# ============================================================
print("\n=== Base Moldings ===")

molding_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.22,'b':0.19,'a':1},
    'roughness':0.5,'metallic':0.15
}

# Left wall molding
safe_add('Architecture/NaveCombiner', 'CSGBox3D', 'MoldingLeft')
safe_set('Architecture/NaveCombiner/MoldingLeft', 'size', {'x':0.4,'y':1.0,'z':36.0})
safe_set('Architecture/NaveCombiner/MoldingLeft', 'position', {'x':-11.0,'y':0.5,'z':0})
safe_set('Architecture/NaveCombiner/MoldingLeft', 'material_override', molding_mat)

# Right wall molding
safe_add('Architecture/NaveCombiner', 'CSGBox3D', 'MoldingRight')
safe_set('Architecture/NaveCombiner/MoldingRight', 'size', {'x':0.4,'y':1.0,'z':36.0})
safe_set('Architecture/NaveCombiner/MoldingRight', 'position', {'x':11.0,'y':0.5,'z':0})
safe_set('Architecture/NaveCombiner/MoldingRight', 'material_override', molding_mat)

# Top cornice (where walls meet vault)
safe_add('Architecture/NaveCombiner', 'CSGBox3D', 'CorniceLeft')
safe_set('Architecture/NaveCombiner/CorniceLeft', 'size', {'x':0.6,'y':1.5,'z':36.0})
safe_set('Architecture/NaveCombiner/CorniceLeft', 'position', {'x':-10.5,'y':13.0,'z':0})
safe_set('Architecture/NaveCombiner/CorniceLeft', 'material_override', molding_mat)

safe_add('Architecture/NaveCombiner', 'CSGBox3D', 'CorniceRight')
safe_set('Architecture/NaveCombiner/CorniceRight', 'size', {'x':0.6,'y':1.5,'z':36.0})
safe_set('Architecture/NaveCombiner/CorniceRight', 'position', {'x':10.5,'y':13.0,'z':0})
safe_set('Architecture/NaveCombiner/CorniceRight', 'material_override', molding_mat)

print("  Moldings complete!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 4 complete!")
