#!/usr/bin/env python3
"""Phase 14: Training grounds, nave ceiling stars, scenic overlook platform,
vine decoration on temple, more atmospheric fireflies near grove."""
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
# 1. Training grounds behind town
# ============================================================
print("=== Training Grounds ===")

add('TownArea', 'Node3D', 'TrainingGround')
add('TownArea/TrainingGround', 'CSGCombiner3D', 'TrainingGeometry')

# Dirt floor
sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', 'TrainingFloor')
ss('TownArea/TrainingGround/TrainingGeometry/TrainingFloor', 'size', {'x':20.0,'y':0.3,'z':15.0})
ss('TownArea/TrainingGround/TrainingGeometry/TrainingFloor', 'position', {'x':-20.0,'y':0.15,'z':120.0})
ss('TownArea/TrainingGround/TrainingGeometry/TrainingFloor', 'use_collision', True)
ss('TownArea/TrainingGround/TrainingGeometry/TrainingFloor', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.20,'b':0.12,'a':1},
    'roughness':0.95,
    'uv1_scale':{'x':6,'y':6,'z':6}
})

# Training dummies (3)
for i, x in enumerate([-24, -20, -16]):
    name = f'TrainDummy{i}'
    # Post
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGCylinder3D', f'{name}_Post')
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Post', 'radius', 0.15)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Post', 'height', 5.0)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Post', 'position', {'x':x,'y':2.5,'z':120.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Post', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.14,'b':0.07,'a':1},
        'roughness':0.85
    })
    # Head
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGSphere3D', f'{name}_Head')
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Head', 'radius', 0.4)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Head', 'position', {'x':x,'y':5.3,'z':120.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}_Head', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.30,'g':0.22,'b':0.10,'a':1},
        'roughness':0.8
    })

# Weapon rack
sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', 'WeaponRack')
ss('TownArea/TrainingGround/TrainingGeometry/WeaponRack', 'size', {'x':4.0,'y':0.3,'z':0.5})
ss('TownArea/TrainingGround/TrainingGeometry/WeaponRack', 'position', {'x':-28.0,'y':2.0,'z':120.0})
ss('TownArea/TrainingGround/TrainingGeometry/WeaponRack', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.07,'a':1},
    'roughness':0.8
})

# Training light
sa('TownArea/TrainingGround', 'OmniLight3D', 'TrainingLight')
ss('TownArea/TrainingGround/TrainingLight', 'position', {'x':-20.0,'y':5.0,'z':120.0})
ss('TownArea/TrainingGround/TrainingLight', 'light_color', {'r':0.6,'g':0.5,'b':0.3,'a':1})
ss('TownArea/TrainingGround/TrainingLight', 'light_energy', 1.0)
ss('TownArea/TrainingGround/TrainingLight', 'omni_range', 15.0)

print("  Training grounds built!")

# ============================================================
# 2. Nave ceiling stars (small lights in the vault)
# ============================================================
print("\n=== Ceiling Stars ===")

star_light_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.8,'g':0.8,'b':1.0,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.6,'g':0.6,'b':1.0,'a':1},
    'emission_energy_multiplier':3.0,
    'roughness':0.1
}

random.seed(13)
for i in range(15):
    x = random.uniform(-8, 8)
    z = random.uniform(-18, 18)
    y = 14.0 + random.uniform(-1, 1)
    name = f'CeilingStar{i:02d}'
    sa('Architecture/NaveCombiner', 'CSGSphere3D', name)
    ss(f'Architecture/NaveCombiner/{name}', 'radius', 0.1)
    ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'Architecture/NaveCombiner/{name}', 'material_override', star_light_mat)

print("  15 ceiling stars added!")

# ============================================================
# 3. Scenic overlook platform on cliff
# ============================================================
print("\n=== Scenic Overlook ===")

sa('TownArea/Terrain', 'CSGBox3D', 'OverlookPlatform')
ss('TownArea/Terrain/OverlookPlatform', 'size', {'x':8.0,'y':0.5,'z':5.0})
ss('TownArea/Terrain/OverlookPlatform', 'position', {'x':-50.0,'y':15.0,'z':100.0})
ss('TownArea/Terrain/OverlookPlatform', 'use_collision', True)
ss('TownArea/Terrain/OverlookPlatform', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.28,'g':0.25,'b':0.22,'a':1},
    'roughness':0.8
})

# Overlook railing
for side, x in [('F', -50.0), ('L', -54.0), ('R', -46.0)]:
    if side == 'F':
        sa('TownArea/Terrain', 'CSGBox3D', 'OverlookRail_F')
        ss('TownArea/Terrain/OverlookRail_F', 'size', {'x':8.0,'y':1.0,'z':0.3})
        ss('TownArea/Terrain/OverlookRail_F', 'position', {'x':-50.0,'y':15.75,'z':97.5})
    else:
        sa('TownArea/Terrain', 'CSGBox3D', f'OverlookRail_{side}')
        ss(f'TownArea/Terrain/OverlookRail_{side}', 'size', {'x':0.3,'y':1.0,'z':5.0})
        ss(f'TownArea/Terrain/OverlookRail_{side}', 'position', {'x':x,'y':15.75,'z':100.0})
    ss(f'TownArea/Terrain/OverlookRail_{side}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.18,'b':0.15,'a':1},
        'roughness':0.6,'metallic':0.15
    })

# Overlook light
sa('TownArea/Terrain', 'OmniLight3D', 'OverlookLight')
ss('TownArea/Terrain/OverlookLight', 'position', {'x':-50.0,'y':16.5,'z':100.0})
ss('TownArea/Terrain/OverlookLight', 'light_color', {'r':0.3,'g':0.3,'b':0.5,'a':1})
ss('TownArea/Terrain/OverlookLight', 'light_energy', 1.0)
ss('TownArea/Terrain/OverlookLight', 'omni_range', 10.0)

# Overlook camera
sa('TownArea/Terrain', 'Camera3D', 'OverlookCamera')
ss('TownArea/Terrain/OverlookCamera', 'position', {'x':-50.0,'y':17.0,'z':102.0})
ss('TownArea/Terrain/OverlookCamera', 'rotation_degrees', {'x':-10,'y':0,'z':0})
ss('TownArea/Terrain/OverlookCamera', 'fov', 80)

print("  Scenic overlook built!")

# ============================================================
# 4. Vine decorations on temple exterior
# ============================================================
print("\n=== Vine Decorations ===")

vine_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.06,'g':0.15,'b':0.04,'a':1},
    'roughness':0.95
}

for side, x in [('L', -10.8), ('R', 10.8)]:
    for i, z in enumerate([19, 21]):
        name = f'Vine_{side}{i}'
        sa('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', name)
        ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'size', {'x':0.2,'y':12.0,'z':0.5})
        ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':x,'y':8.0,'z':z})
        ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', vine_mat)

print("  4 vine strips added!")

# ============================================================
# 5. Grove fireflies
# ============================================================
print("\n=== Grove Fireflies ===")

grove_fly_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.3,'g':0.6,'b':0.2,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.2,'g':0.6,'b':0.1,'a':1},
    'emission_energy_multiplier':6.0,
    'roughness':0.1
}

random.seed(88)
for i in range(20):
    x = -30.0 + random.uniform(-6, 6)
    y = random.uniform(0.5, 5.0)
    z = 100.0 + random.uniform(-6, 6)
    name = f'GroveFly{i:02d}'
    sa('TownArea/HiddenGrove/GroveGeometry', 'CSGSphere3D', name)
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}', 'radius', 0.08)
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}', 'material_override', grove_fly_mat)

print(f"  20 grove fireflies placed!")

# ============================================================
# 6. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 14 complete!")
