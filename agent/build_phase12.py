#!/usr/bin/env python3
"""Phase 12: Windmill, shrine by lake, campfire, crystal cave entrance,
more vegetation, decorative carvings on temple."""
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
# 1. Windmill in town
# ============================================================
print("=== Windmill ===")

wood_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.22,'b':0.12,'a':1},
    'roughness':0.8
}

# Tower base
sa('TownArea/TownSquare', 'CSGCylinder3D', 'WindmillBase')
ss('TownArea/TownSquare/WindmillBase', 'radius', 3.0)
ss('TownArea/TownSquare/WindmillBase', 'height', 8.0)
ss('TownArea/TownSquare/WindmillBase', 'sides', 12)
ss('TownArea/TownSquare/WindmillBase', 'position', {'x':48.0,'y':4.0,'z':115.0})
ss('TownArea/TownSquare/WindmillBase', 'use_collision', True)
ss('TownArea/TownSquare/WindmillBase', 'material_override', wood_mat)

# Cone roof
sa('TownArea/TownSquare', 'CSGCylinder3D', 'WindmillRoof')
ss('TownArea/TownSquare/WindmillRoof', 'radius', 3.2)
ss('TownArea/TownSquare/WindmillRoof', 'height', 3.0)
ss('TownArea/TownSquare/WindmillRoof', 'sides', 12)
ss('TownArea/TownSquare/WindmillRoof', 'position', {'x':48.0,'y':11.5,'z':115.0})
ss('TownArea/TownSquare/WindmillRoof', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.12,'b':0.08,'a':1},
    'roughness':0.8
})

# Windmill blades (4 crossed beams)
for i in range(4):
    angle = i * 90
    name = f'WindmillBlade{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':0.3,'y':6.0,'z':0.3})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':48.0,'y':8.0,'z':118.5})
    ss(f'TownArea/TownSquare/{name}', 'rotation_degrees', {'x':0,'y':0,'z':angle})
    ss(f'TownArea/TownSquare/{name}', 'material_override', wood_mat)

# Blade center hub
sa('TownArea/TownSquare', 'CSGCylinder3D', 'WindmillHub')
ss('TownArea/TownSquare/WindmillHub', 'radius', 0.5)
ss('TownArea/TownSquare/WindmillHub', 'height', 0.5)
ss('TownArea/TownSquare/WindmillHub', 'position', {'x':48.0,'y':8.0,'z':118.5})
ss('TownArea/TownSquare/WindmillHub', 'rotation_degrees', {'x':90,'y':0,'z':0})
ss('TownArea/TownSquare/WindmillHub', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.10,'b':0.05,'a':1},
    'roughness':0.4,'metallic':0.5
})

print("  Windmill built!")

# ============================================================
# 2. Shrine by the lake
# ============================================================
print("\n=== Lake Shrine ===")

shrine_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.28,'b':0.25,'a':1},
    'roughness':0.7,'metallic':0.1
}

# Platform
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'ShrinePlatform')
ss('TownArea/LakeRegion/LakeGeometry/ShrinePlatform', 'size', {'x':6.0,'y':0.5,'z':6.0})
ss('TownArea/LakeRegion/LakeGeometry/ShrinePlatform', 'position', {'x':15.0,'y':0.25,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/ShrinePlatform', 'material_override', shrine_mat)

# 4 corner posts
for i in range(4):
    angle = i * math.pi / 2 + math.pi / 4
    x = 15.0 + math.cos(angle) * 2.5
    z = 175.0 + math.sin(angle) * 2.5
    name = f'ShrinePost{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':0.4,'y':5.0,'z':0.4})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':x,'y':2.5,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', shrine_mat)

# Roof
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'ShrineRoof')
ss('TownArea/LakeRegion/LakeGeometry/ShrineRoof', 'size', {'x':7.0,'y':0.4,'z':7.0})
ss('TownArea/LakeRegion/LakeGeometry/ShrineRoof', 'position', {'x':15.0,'y':5.2,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/ShrineRoof', 'material_override', shrine_mat)

# Glowing offering stone
sa('TownArea/LakeRegion/LakeGeometry', 'CSGSphere3D', 'ShrineStone')
ss('TownArea/LakeRegion/LakeGeometry/ShrineStone', 'radius', 0.6)
ss('TownArea/LakeRegion/LakeGeometry/ShrineStone', 'position', {'x':15.0,'y':1.0,'z':175.0})
ss('TownArea/LakeRegion/LakeGeometry/ShrineStone', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.1,'g':0.3,'b':0.5,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.4,'b':0.8,'a':1},
    'emission_energy_multiplier':3.0,
    'roughness':0.2,'metallic':0.0
})

# Shrine light
sa('TownArea/LakeRegion', 'OmniLight3D', 'ShrineLight')
ss('TownArea/LakeRegion/ShrineLight', 'position', {'x':15.0,'y':2.0,'z':175.0})
ss('TownArea/LakeRegion/ShrineLight', 'light_color', {'r':0.15,'g':0.4,'b':0.8,'a':1})
ss('TownArea/LakeRegion/ShrineLight', 'light_energy', 2.0)
ss('TownArea/LakeRegion/ShrineLight', 'omni_range', 15.0)

print("  Lake shrine built!")

# ============================================================
# 3. Campfire near forest path
# ============================================================
print("\n=== Campfire ===")

# Fire ring
sa('TownArea/Terrain', 'CSGCylinder3D', 'CampfireRing')
ss('TownArea/Terrain/CampfireRing', 'radius', 1.5)
ss('TownArea/Terrain/CampfireRing', 'height', 0.3)
ss('TownArea/Terrain/CampfireRing', 'position', {'x':28.0,'y':0.15,'z':155.0})
ss('TownArea/Terrain/CampfireRing', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.18,'b':0.15,'a':1},
    'roughness':0.9
})

# Logs
for i in range(4):
    angle = i * 45
    name = f'CampfireLog{i}'
    sa('TownArea/Terrain', 'CSGCylinder3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.15)
    ss(f'TownArea/Terrain/{name}', 'height', 2.0)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':28.0,'y':0.3,'z':155.0})
    ss(f'TownArea/Terrain/{name}', 'rotation_degrees', {'x':90,'y':angle,'z':0})
    ss(f'TownArea/Terrain/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.12,'b':0.05,'a':1},
        'roughness':0.85
    })

# Flame
sa('TownArea/Terrain', 'CSGSphere3D', 'CampfireFlame')
ss('TownArea/Terrain/CampfireFlame', 'radius', 0.8)
ss('TownArea/Terrain/CampfireFlame', 'position', {'x':28.0,'y':1.0,'z':155.0})
ss('TownArea/Terrain/CampfireFlame', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':1.0,'g':0.5,'b':0.1,'a':1},
    'emission_enabled':True,
    'emission':{'r':1.0,'g':0.4,'b':0.05,'a':1},
    'emission_energy_multiplier':5.0,
    'roughness':0.2
})

# Fire light
sa('TownArea/Terrain', 'OmniLight3D', 'CampfireLight')
ss('TownArea/Terrain/CampfireLight', 'position', {'x':28.0,'y':2.0,'z':155.0})
ss('TownArea/Terrain/CampfireLight', 'light_color', {'r':1.0,'g':0.6,'b':0.2,'a':1})
ss('TownArea/Terrain/CampfireLight', 'light_energy', 4.0)
ss('TownArea/Terrain/CampfireLight', 'omni_range', 20.0)

# Seating logs around fire
for i in range(3):
    angle = i * 120 + 60
    x = 28.0 + math.cos(math.radians(angle)) * 3.0
    z = 155.0 + math.sin(math.radians(angle)) * 3.0
    name = f'SeatLog{i}'
    sa('TownArea/Terrain', 'CSGCylinder3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.25)
    ss(f'TownArea/Terrain/{name}', 'height', 2.5)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.25,'z':z})
    ss(f'TownArea/Terrain/{name}', 'rotation_degrees', {'x':90,'y':angle,'z':0})
    ss(f'TownArea/Terrain/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.25,'g':0.15,'b':0.07,'a':1},
        'roughness':0.85
    })

print("  Campfire built!")

# ============================================================
# 4. Crystal cave entrance near the lake
# ============================================================
print("\n=== Crystal Cave ===")

cave_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.12,'g':0.10,'b':0.10,'a':1},
    'roughness':0.95
}

# Cave mound
sa('TownArea/LakeRegion/LakeGeometry', 'CSGSphere3D', 'CaveMound')
ss('TownArea/LakeRegion/LakeGeometry/CaveMound', 'radius', 8.0)
ss('TownArea/LakeRegion/LakeGeometry/CaveMound', 'position', {'x':50.0,'y':3.0,'z':170.0})
ss('TownArea/LakeRegion/LakeGeometry/CaveMound', 'material_override', cave_mat)

# Cave entrance (dark box)
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'CaveEntry')
ss('TownArea/LakeRegion/LakeGeometry/CaveEntry', 'size', {'x':3.0,'y':4.0,'z':3.0})
ss('TownArea/LakeRegion/LakeGeometry/CaveEntry', 'position', {'x':50.0,'y':2.0,'z':163.0})
ss('TownArea/LakeRegion/LakeGeometry/CaveEntry', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.01,'g':0.01,'b':0.02,'a':1},
    'roughness':1.0
})

# Glowing crystals around entrance
crystal_colors = [
    {'r':0.1,'g':0.4,'b':0.8,'a':1},
    {'r':0.4,'g':0.1,'b':0.6,'a':1},
    {'r':0.1,'g':0.6,'b':0.4,'a':1},
]

random.seed(33)
for i in range(8):
    angle = random.uniform(-math.pi/3, math.pi/3)
    r = random.uniform(2.5, 4.0)
    x = 50.0 + math.sin(angle) * r
    z = 163.0 + math.cos(angle) * r
    y = random.uniform(0.5, 3.0)
    h = random.uniform(1.0, 2.5)
    color = crystal_colors[i % 3]
    name = f'Crystal{i:02d}'
    
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':0.3,'y':h,'z':0.3})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'rotation_degrees', {'x':random.uniform(-10,10),'y':random.uniform(0,360),'z':random.uniform(-15,15)})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':color,
        'emission_enabled':True,
        'emission':color,
        'emission_energy_multiplier':3.0,
        'roughness':0.1,'metallic':0.0,
        'transparency':2
    })

# Cave glow
sa('TownArea/LakeRegion', 'OmniLight3D', 'CaveGlow')
ss('TownArea/LakeRegion/CaveGlow', 'position', {'x':50.0,'y':2.0,'z':164.0})
ss('TownArea/LakeRegion/CaveGlow', 'light_color', {'r':0.2,'g':0.3,'b':0.6,'a':1})
ss('TownArea/LakeRegion/CaveGlow', 'light_energy', 2.0)
ss('TownArea/LakeRegion/CaveGlow', 'omni_range', 12.0)

print("  Crystal cave entrance built!")

# ============================================================
# 5. More vegetation — ferns and tall grass
# ============================================================
print("\n=== Ferns & Tall Grass ===")

fern_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.06,'g':0.20,'b':0.05,'a':1},
    'roughness':0.95
}

random.seed(77)
for i in range(25):
    x = random.uniform(-15, 60)
    z = random.uniform(40, 170)
    y = 0.3
    name = f'Fern{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', random.uniform(0.4, 0.8))
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', fern_mat)

print(f"  25 ferns placed!")

# ============================================================
# 6. Decorative carvings on temple exterior (relief panels)
# ============================================================
print("\n=== Temple Relief Panels ===")

relief_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.22,'b':0.19,'a':1},
    'roughness':0.6,'metallic':0.1
}

# Relief panels on front wall
for i, x in enumerate([-6, 0, 6]):
    name = f'ReliefPanel{i}'
    sa('Architecture/Exterior/ExteriorPlaza/FrontWall', 'CSGBox3D', name)
    ss(f'Architecture/Exterior/ExteriorPlaza/FrontWall/{name}', 'size', {'x':3.0,'y':3.0,'z':0.15})
    ss(f'Architecture/Exterior/ExteriorPlaza/FrontWall/{name}', 'position', {'x':x,'y':14.0,'z':0.6})
    ss(f'Architecture/Exterior/ExteriorPlaza/FrontWall/{name}', 'material_override', relief_mat)

print("  3 relief panels added!")

# ============================================================
# 7. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 12 complete!")
