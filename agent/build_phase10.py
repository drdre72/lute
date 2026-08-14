#!/usr/bin/env python3
"""Phase 10: Lake region between forest and castle, castle bridge,
ruined temple columns in the forest, more atmospheric detail."""
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
# 1. Lake between forest and castle
# ============================================================
print("=== Lake ===")

add('TownArea', 'Node3D', 'LakeRegion')
add('TownArea/LakeRegion', 'CSGCombiner3D', 'LakeGeometry')

# Lake surface
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'LakeSurface')
ss('TownArea/LakeRegion/LakeGeometry/LakeSurface', 'size', {'x':50.0,'y':0.3,'z':25.0})
ss('TownArea/LakeRegion/LakeGeometry/LakeSurface', 'position', {'x':30.0,'y':0.1,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/LakeSurface', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.03,'g':0.10,'b':0.20,'a':0.6},
    'roughness':0.05,'metallic':0.0,
    'transparency':2,
    'emission_enabled':True,
    'emission':{'r':0.02,'g':0.06,'b':0.12,'a':1},
    'emission_energy_multiplier':0.4
})

# Lake shore (darker ground ring)
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'LakeShore')
ss('TownArea/LakeRegion/LakeGeometry/LakeShore', 'size', {'x':55.0,'y':0.2,'z':30.0})
ss('TownArea/LakeRegion/LakeGeometry/LakeShore', 'position', {'x':30.0,'y':0.0,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/LakeShore', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.12,'b':0.08,'a':1},
    'roughness':0.95,
    'uv1_scale':{'x':10,'y':10,'z':10}
})

# Lake mist light
sa('TownArea/LakeRegion', 'OmniLight3D', 'LakeMist')
ss('TownArea/LakeRegion/LakeMist', 'position', {'x':30.0,'y':5.0,'z':175.0})
ss('TownArea/LakeRegion/LakeMist', 'light_color', {'r':0.08,'g':0.12,'b':0.2,'a':1})
ss('TownArea/LakeRegion/LakeMist', 'light_energy', 0.8)
ss('TownArea/LakeRegion/LakeMist', 'omni_range', 30.0)

# Reeds at lake edge
reed_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.12,'g':0.20,'b':0.08,'a':1},
    'roughness':0.95
}

random.seed(99)
for i in range(15):
    angle = random.uniform(0, 2 * math.pi)
    r = random.uniform(20, 26)
    x = 30.0 + math.cos(angle) * r
    z = 175.0 + math.sin(angle) * r * 0.5
    name = f'Reed{i:02d}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGCylinder3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'radius', 0.1)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'height', 2.0)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':x,'y':1.0,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', reed_mat)

print("  Lake region built!")

# ============================================================
# 2. Bridge across the lake to the castle
# ============================================================
print("\n=== Castle Bridge ===")

bridge_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.28,'g':0.25,'b':0.22,'a':1},
    'roughness':0.8,'metallic':0.05,
    'uv1_scale':{'x':2,'y':1,'z':2}
}

# Bridge deck
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'CastleBridge')
ss('TownArea/LakeRegion/LakeGeometry/CastleBridge', 'size', {'x':5.0,'y':0.6,'z':20.0})
ss('TownArea/LakeRegion/LakeGeometry/CastleBridge', 'position', {'x':30.0,'y':0.5,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/CastleBridge', 'use_collision', True)
ss('TownArea/LakeRegion/LakeGeometry/CastleBridge', 'material_override', bridge_mat)

# Bridge arches (support pillars)
for i, z in enumerate(range(168, 184, 4)):
    name = f'BridgeArch{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':5.5,'y':2.0,'z':0.5})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':30.0,'y':-0.5,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', bridge_mat)

# Bridge railings
for side, x in [('L', 27.0), ('R', 33.0)]:
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', f'CastleBridgeRail_{side}')
    ss(f'TownArea/LakeRegion/LakeGeometry/CastleBridgeRail_{side}', 'size', {'x':0.3,'y':1.2,'z':20.0})
    ss(f'TownArea/LakeRegion/LakeGeometry/CastleBridgeRail_{side}', 'position', {'x':x,'y':1.4,'z':175.0})
    ss(f'TownArea/LakeRegion/LakeGeometry/CastleBridgeRail_{side}', 'material_override', bridge_mat)

print("  Castle bridge built!")

# ============================================================
# 3. Ruined columns in the forest (ancient ruins)
# ============================================================
print("\n=== Forest Ruins ===")

ruin_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.28,'g':0.26,'b':0.22,'a':1},
    'roughness':0.9,'metallic':0.03
}

ruin_positions = [
    (12, 160, 4.0, 30), (16, 165, 3.0, 15), (48, 160, 5.0, 45),
    (44, 168, 3.5, 60), (10, 170, 2.5, 90), (52, 172, 4.0, 120),
]

for i, (x, z, r, rot) in enumerate(ruin_positions):
    name = f'RuinColumn{i:02d}'
    sa('TownArea/Terrain', 'CSGCylinder3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', r * 0.3)
    ss(f'TownArea/Terrain/{name}', 'height', r)
    ss(f'TownArea/Terrain/{name}', 'sides', 8)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':r/2,'z':z})
    ss(f'TownArea/Terrain/{name}', 'rotation_degrees', {'x':random.uniform(-5,5),'y':rot,'z':random.uniform(-3,3)})
    ss(f'TownArea/Terrain/{name}', 'material_override', ruin_mat)

# Broken arch (two columns + fallen lintel)
sa('TownArea/Terrain', 'CSGBox3D', 'RuinArch1')
ss('TownArea/Terrain/RuinArch1', 'size', {'x':0.8,'y':4.0,'z':0.8})
ss('TownArea/Terrain/RuinArch1', 'position', {'x':14.0,'y':2.0,'z':162.0})
ss('TownArea/Terrain/RuinArch1', 'rotation_degrees', {'x':0,'y':0,'z':3})
ss('TownArea/Terrain/RuinArch1', 'material_override', ruin_mat)

sa('TownArea/Terrain', 'CSGBox3D', 'RuinArch2')
ss('TownArea/Terrain/RuinArch2', 'size', {'x':0.8,'y':3.5,'z':0.8})
ss('TownArea/Terrain/RuinArch2', 'position', {'x':18.0,'y':1.75,'z':162.0})
ss('TownArea/Terrain/RuinArch2', 'rotation_degrees', {'x':0,'y':0,'z':-2})
ss('TownArea/Terrain/RuinArch2', 'material_override', ruin_mat)

# Fallen lintel
sa('TownArea/Terrain', 'CSGBox3D', 'RuinLintel')
ss('TownArea/Terrain/RuinLintel', 'size', {'x':5.0,'y':0.8,'z':0.8})
ss('TownArea/Terrain/RuinLintel', 'position', {'x':16.0,'y':0.4,'z':164.0})
ss('TownArea/Terrain/RuinLintel', 'rotation_degrees', {'x':0,'y':0,'z':15})
ss('TownArea/Terrain/RuinLintel', 'material_override', ruin_mat)

print("  Forest ruins added!")

# ============================================================
# 4. Nave interior refinement — altar cloth, prayer candles
# ============================================================
print("\n=== Altar Details ===")

# Altar cloth (fabric on pedestal table)
sa('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'CSGBox3D', 'AltarCloth')
ss('Architecture/NaveCombiner/AltarPlatform/PedestalTable/AltarCloth', 'size', {'x':3.2,'y':0.05,'z':1.4})
ss('Architecture/NaveCombiner/AltarPlatform/PedestalTable/AltarCloth', 'position', {'x':0,'y':0.58,'z':0})
ss('Architecture/NaveCombiner/AltarPlatform/PedestalTable/AltarCloth', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.4,'g':0.25,'b':0.15,'a':1},
    'roughness':0.9,'metallic':0.0,
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.05,'b':0.02,'a':1},
    'emission_energy_multiplier':0.3
})

# Prayer candles around altar (ring of small flames)
for i in range(8):
    angle = i * math.pi / 4
    x = math.cos(angle) * 3.5
    z = 20.8 + math.sin(angle) * 1.0
    name = f'PrayerCandle{i}'
    
    # Candle wax
    sa('Architecture/NaveCombiner', 'CSGCylinder3D', f'{name}_Wax')
    ss(f'Architecture/NaveCombiner/{name}_Wax', 'radius', 0.08)
    ss(f'Architecture/NaveCombiner/{name}_Wax', 'height', 0.5)
    ss(f'Architecture/NaveCombiner/{name}_Wax', 'position', {'x':x,'y':1.35,'z':z})
    ss(f'Architecture/NaveCombiner/{name}_Wax', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.9,'g':0.85,'b':0.7,'a':1},
        'roughness':0.6
    })
    
    # Flame
    sa('Architecture/NaveCombiner', 'CSGSphere3D', f'{name}_Flame')
    ss(f'Architecture/NaveCombiner/{name}_Flame', 'radius', 0.08)
    ss(f'Architecture/NaveCombiner/{name}_Flame', 'position', {'x':x,'y':1.65,'z':z})
    ss(f'Architecture/NaveCombiner/{name}_Flame', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':1.0,'g':0.6,'b':0.1,'a':1},
        'emission_enabled':True,
        'emission':{'r':1.0,'g':0.5,'b':0.1,'a':1},
        'emission_energy_multiplier':4.0,
        'roughness':0.2
    })

print("  Altar details added!")

# ============================================================
# 5. Stars in the sky (small emissive spheres high up)
# ============================================================
print("\n=== Stars ===")

star_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':1.0,'g':1.0,'b':1.0,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.8,'g':0.8,'b':1.0,'a':1},
    'emission_energy_multiplier':2.0,
    'roughness':0.1
}

random.seed(7)
for i in range(40):
    x = random.uniform(-80, 80)
    y = random.uniform(40, 80)
    z = random.uniform(30, 200)
    name = f'Star{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.3)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', star_mat)

print(f"  40 stars placed!")

# ============================================================
# 6. Moon
# ============================================================
print("\n=== Moon ===")

sa('TownArea/Terrain', 'CSGSphere3D', 'Moon')
ss('TownArea/Terrain/Moon', 'radius', 5.0)
ss('TownArea/Terrain/Moon', 'position', {'x':-50.0,'y':60.0,'z':120.0})
ss('TownArea/Terrain/Moon', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.9,'g':0.9,'b':0.85,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.5,'g':0.5,'b':0.45,'a':1},
    'emission_energy_multiplier':1.5,
    'roughness':0.8
})

print("  Moon placed!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 10 complete!")
