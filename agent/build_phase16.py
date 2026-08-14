#!/usr/bin/env python3
"""Phase 16: Fix dock right posts, add scarecrow in garden, town notice board,
fishing rod on dock, more nave detail — altar steps railing, throne chair."""
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
# 1. Fix dock right posts
# ============================================================
print("=== Fix Dock Right Posts ===")

for i, z in enumerate(range(168, 177, 2)):
    name = f'DockPostR{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':0.2,'y':1.5,'z':0.2})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':21.2,'y':0.5,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.30,'g':0.20,'b':0.10,'a':1},
        'roughness':0.8
    })

print("  Dock posts fixed!")

# ============================================================
# 2. Scarecrow in garden
# ============================================================
print("\n=== Scarecrow ===")

sa('TownArea/TownSquare', 'CSGCylinder3D', 'ScarecrowPost')
ss('TownArea/TownSquare/ScarecrowPost', 'radius', 0.1)
ss('TownArea/TownSquare/ScarecrowPost', 'height', 4.0)
ss('TownArea/TownSquare/ScarecrowPost', 'position', {'x':10.0,'y':2.0,'z':115.0})
ss('TownArea/TownSquare/ScarecrowPost', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.07,'a':1},
    'roughness':0.85
})

# Crossbar
sa('TownArea/TownSquare', 'CSGBox3D', 'ScarecrowArms')
ss('TownArea/TownSquare/ScarecrowArms', 'size', {'x':3.0,'y':0.15,'z':0.15})
ss('TownArea/TownSquare/ScarecrowArms', 'position', {'x':10.0,'y':3.0,'z':115.0})
ss('TownArea/TownSquare/ScarecrowArms', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.07,'a':1},
    'roughness':0.85
})

# Head
sa('TownArea/TownSquare', 'CSGSphere3D', 'ScarecrowHead')
ss('TownArea/TownSquare/ScarecrowHead', 'radius', 0.4)
ss('TownArea/TownSquare/ScarecrowHead', 'position', {'x':10.0,'y':4.3,'z':115.0})
ss('TownArea/TownSquare/ScarecrowHead', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.5,'g':0.4,'b':0.2,'a':1},
    'roughness':0.95
})

print("  Scarecrow built!")

# ============================================================
# 3. Town notice board
# ============================================================
print("\n=== Notice Board ===")

sa('TownArea/TownSquare', 'CSGBox3D', 'NoticeBoard')
ss('TownArea/TownSquare/NoticeBoard', 'size', {'x':2.5,'y':3.0,'z':0.15})
ss('TownArea/TownSquare/NoticeBoard', 'position', {'x':12.0,'y':2.5,'z':105.0})
ss('TownArea/TownSquare/NoticeBoard', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.20,'b':0.08,'a':1},
    'roughness':0.7
})

# Posts
for side, x in [('L', 11.0), ('R', 13.0)]:
    sa('TownArea/TownSquare', 'CSGBox3D', f'NoticePost_{side}')
    ss(f'TownArea/TownSquare/NoticePost_{side}', 'size', {'x':0.15,'y':4.0,'z':0.15})
    ss(f'TownArea/TownSquare/NoticePost_{side}', 'position', {'x':x,'y':2.0,'z':105.0})
    ss(f'TownArea/TownSquare/NoticePost_{side}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.14,'b':0.07,'a':1},
        'roughness':0.8
    })

print("  Notice board placed!")

# ============================================================
# 4. Altar steps railing
# ============================================================
print("\n=== Altar Railings ===")

railing_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.18,'b':0.15,'a':1},
    'roughness':0.4,'metallic':0.3
}

for side, x in [('L', -3.5), ('R', 3.5)]:
    name = f'AltarRail_{side}'
    sa('Architecture/NaveCombiner', 'CSGBox3D', name)
    ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.2,'y':1.0,'z':8.0})
    ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':0.5,'z':17.0})
    ss(f'Architecture/NaveCombiner/{name}', 'material_override', railing_mat)

print("  Altar railings added!")

# ============================================================
# 5. Throne chair behind altar
# ============================================================
print("\n=== Throne ===")

throne_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.10,'b':0.08,'a':1},
    'roughness':0.5,'metallic':0.2,
    'emission_enabled':True,
    'emission':{'r':0.05,'g':0.02,'b':0.01,'a':1},
    'emission_energy_multiplier':0.3
}

# Seat
sa('Architecture/NaveCombiner', 'CSGBox3D', 'ThroneSeat')
ss('Architecture/NaveCombiner/ThroneSeat', 'size', {'x':2.0,'y':1.5,'z':2.0})
ss('Architecture/NaveCombiner/ThroneSeat', 'position', {'x':0,'y':1.25,'z':24.0})
ss('Architecture/NaveCombiner/ThroneSeat', 'material_override', throne_mat)

# Back
sa('Architecture/NaveCombiner', 'CSGBox3D', 'ThroneBack')
ss('Architecture/NaveCombiner/ThroneBack', 'size', {'x':2.0,'y':4.0,'z':0.5})
ss('Architecture/NaveCombiner/ThroneBack', 'position', {'x':0,'y':3.0,'z':24.8})
ss('Architecture/NaveCombiner/ThroneBack', 'material_override', throne_mat)

# Arm rests
for side, x in [('L', -1.3), ('R', 1.3)]:
    sa('Architecture/NaveCombiner', 'CSGBox3D', f'ThroneArm_{side}')
    ss(f'Architecture/NaveCombiner/ThroneArm_{side}', 'size', {'x':0.5,'y':0.5,'z':2.0})
    ss(f'Architecture/NaveCombiner/ThroneArm_{side}', 'position', {'x':x,'y':1.75,'z':24.0})
    ss(f'Architecture/NaveCombiner/ThroneArm_{side}', 'material_override', throne_mat)

# Throne glow
sa('.', 'OmniLight3D', 'ThroneGlow')
ss('ThroneGlow', 'position', {'x':0,'y':3.0,'z':24.0})
ss('ThroneGlow', 'light_color', {'r':0.5,'g':0.3,'b':0.1,'a':1})
ss('ThroneGlow', 'light_energy', 1.5)
ss('ThroneGlow', 'omni_range', 8.0)

print("  Throne placed behind altar!")

# ============================================================
# 6. Fishing rod on dock
# ============================================================
print("\n=== Fishing Rod ===")

sa('TownArea/LakeRegion/LakeGeometry', 'CSGCylinder3D', 'FishingRod')
ss('TownArea/LakeRegion/LakeGeometry/FishingRod', 'radius', 0.03)
ss('TownArea/LakeRegion/LakeGeometry/FishingRod', 'height', 4.0)
ss('TownArea/LakeRegion/LakeGeometry/FishingRod', 'position', {'x':20.0,'y':2.3,'z':170.0})
ss('TownArea/LakeRegion/LakeGeometry/FishingRod', 'rotation_degrees', {'x':30,'y':0,'z':0})
ss('TownArea/LakeRegion/LakeGeometry/FishingRod', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.12,'b':0.05,'a':1},
    'roughness':0.7
})

print("  Fishing rod placed!")

# ============================================================
# 7. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 16 complete!")
