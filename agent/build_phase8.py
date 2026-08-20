#!/usr/bin/env python3
"""Phase 8: Window tracery, hanging ceiling lamps, wall weathering strips,
gargoyles on exterior, well in town square, more buildings."""
import sys, os
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
# 1. Hanging ceiling lamps (warm point lights dangling from ribs)
# ============================================================
print("=== Hanging Ceiling Lamps ===")

lamp_metal = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.12,'b':0.08,'a':1},
    'roughness':0.3,'metallic':0.7
}

for i, z in enumerate([15, 9, 3, -3, -9, -15]):
    name = f'HangingLamp{i}'
    # Chain
    sa('Architecture/NaveCombiner', 'CSGBox3D', f'{name}_Chain')
    ss(f'Architecture/NaveCombiner/{name}_Chain', 'size', {'x':0.1,'y':3.0,'z':0.1})
    ss(f'Architecture/NaveCombiner/{name}_Chain', 'position', {'x':0,'y':13.0,'z':z})
    ss(f'Architecture/NaveCombiner/{name}_Chain', 'material_override', lamp_metal)
    
    # Lamp body
    sa('Architecture/NaveCombiner', 'CSGSphere3D', f'{name}_Body')
    ss(f'Architecture/NaveCombiner/{name}_Body', 'radius', 0.4)
    ss(f'Architecture/NaveCombiner/{name}_Body', 'position', {'x':0,'y':11.5,'z':z})
    ss(f'Architecture/NaveCombiner/{name}_Body', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.2,'g':0.15,'b':0.05,'a':1},
        'emission_enabled':True,
        'emission':{'r':0.8,'g':0.5,'b':0.2,'a':1},
        'emission_energy_multiplier':2.0,
        'roughness':0.4,'metallic':0.3
    })
    
    # Light
    sa('.', 'OmniLight3D', f'{name}_Light')
    ss(f'{name}_Light', 'position', {'x':0,'y':11.3,'z':z})
    ss(f'{name}_Light', 'light_color', {'r':1.0,'g':0.75,'b':0.4,'a':1})
    ss(f'{name}_Light', 'light_energy', 2.0)
    ss(f'{name}_Light', 'omni_range', 14.0)
    ss(f'{name}_Light', 'omni_attenuation', 1.5)

print("  6 hanging lamps added!")

# ============================================================
# 2. Window tracery (stone frames around stained glass)
# ============================================================
print("\n=== Window Tracery ===")

tracery_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.27,'b':0.24,'a':1},
    'roughness':0.75,'metallic':0.05
}

for side in ['L', 'R']:
    for i in range(5):
        win_name = f'Window{side}{i}'
        win_path = f'Architecture/NaveCombiner/{win_name}'
        x = -11.0 if side == 'L' else 11.0
        
        # Top arch point
        sa(win_path, 'CSGBox3D', f'{win_name}_FrameTop')
        ss(f'{win_path}/{win_name}_FrameTop', 'size', {'x':0.15,'y':0.5,'z':3.4})
        ss(f'{win_path}/{win_name}_FrameTop', 'position', {'x':0,'y':3.25,'z':0})
        ss(f'{win_path}/{win_name}_FrameTop', 'material_override', tracery_mat)
        
        # Left frame
        sa(win_path, 'CSGBox3D', f'{win_name}_FrameL')
        ss(f'{win_path}/{win_name}_FrameL', 'size', {'x':0.15,'y':6.4,'z':0.3})
        ss(f'{win_path}/{win_name}_FrameL', 'position', {'x':0,'y':0,'z':-1.5})
        ss(f'{win_path}/{win_name}_FrameL', 'material_override', tracery_mat)
        
        # Right frame
        sa(win_path, 'CSGBox3D', f'{win_name}_FrameR')
        ss(f'{win_path}/{win_name}_FrameR', 'size', {'x':0.15,'y':6.4,'z':0.3})
        ss(f'{win_path}/{win_name}_FrameR', 'position', {'x':0,'y':0,'z':1.5})
        ss(f'{win_path}/{win_name}_FrameR', 'material_override', tracery_mat)
        
        # Middle mullion (vertical divider)
        sa(win_path, 'CSGBox3D', f'{win_name}_Mullion')
        ss(f'{win_path}/{win_name}_Mullion', 'size', {'x':0.15,'y':6.0,'z':0.15})
        ss(f'{win_path}/{win_name}_Mullion', 'position', {'x':0,'y':0,'z':0})
        ss(f'{win_path}/{win_name}_Mullion', 'material_override', tracery_mat)
        
        # Horizontal divider
        sa(win_path, 'CSGBox3D', f'{win_name}_Divider')
        ss(f'{win_path}/{win_name}_Divider', 'size', {'x':0.15,'y':0.15,'z':3.0})
        ss(f'{win_path}/{win_name}_Divider', 'position', {'x':0,'y':1.5,'z':0})
        ss(f'{win_path}/{win_name}_Divider', 'material_override', tracery_mat)

print("  Window tracery on all 10 windows!")

# ============================================================
# 3. Wall weathering — dark strips at base of nave walls
# ============================================================
print("\n=== Wall Weathering ===")

weather_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.07,'b':0.06,'a':1},
    'roughness':0.95,'metallic':0.0
}

for side, x in [('L', -11.05), ('R', 11.05)]:
    name = f'WeatherStrip_{side}'
    sa('Architecture/NaveCombiner', 'CSGBox3D', name)
    ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.05,'y':3.0,'z':36.0})
    ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':1.5,'z':0})
    ss(f'Architecture/NaveCombiner/{name}', 'material_override', weather_mat)

print("  Wall weathering added!")

# ============================================================
# 4. Gargoyles on exterior facade
# ============================================================
print("\n=== Exterior Gargoyles ===")

gargoyle_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.22,'g':0.20,'b':0.18,'a':1},
    'roughness':0.85,'metallic':0.05
}

for side, x in [('L', -10.0), ('R', 10.0)]:
    name = f'Gargoyle_{side}'
    sa('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', name)
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'size', {'x':1.0,'y':1.5,'z':2.0})
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':x,'y':16.0,'z':20.0})
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', gargoyle_mat)

print("  2 gargoyles placed!")

# ============================================================
# 5. Well in town square
# ============================================================
print("\n=== Town Well ===")

well_stone = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.32,'b':0.28,'a':1},
    'roughness':0.8,'metallic':0.05
}

# Well ring
sa('TownArea/TownSquare', 'CSGCylinder3D', 'WellBase')
ss('TownArea/TownSquare/WellBase', 'radius', 2.0)
ss('TownArea/TownSquare/WellBase', 'height', 1.2)
ss('TownArea/TownSquare/WellBase', 'position', {'x':30.0,'y':0.6,'z':115.0})
ss('TownArea/TownSquare/WellBase', 'material_override', well_stone)

# Water
sa('TownArea/TownSquare', 'CSGCylinder3D', 'WellWater')
ss('TownArea/TownSquare/WellWater', 'radius', 1.7)
ss('TownArea/TownSquare/WellWater', 'height', 0.8)
ss('TownArea/TownSquare/WellWater', 'position', {'x':30.0,'y':0.5,'z':115.0})
ss('TownArea/TownSquare/WellWater', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.15,'b':0.3,'a':0.6},
    'roughness':0.1,'metallic':0.0,
    'transparency':2,
    'emission_enabled':True,
    'emission':{'r':0.02,'g':0.08,'b':0.15,'a':1},
    'emission_energy_multiplier':0.3
})

# Well roof posts
for i in range(4):
    import math
    angle = i * math.pi / 2
    px = 30.0 + math.cos(angle) * 2.0
    pz = 115.0 + math.sin(angle) * 2.0
    name = f'WellPost{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':0.2,'y':4.0,'z':0.2})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':px,'y':2.0,'z':pz})
    ss(f'TownArea/TownSquare/{name}', 'material_override', well_stone)

# Well roof
sa('TownArea/TownSquare', 'CSGCone3D', 'WellRoof')
ss('TownArea/TownSquare/WellRoof', 'radius', 2.5)
ss('TownArea/TownSquare/WellRoof', 'height', 2.0)
ss('TownArea/TownSquare/WellRoof', 'sides', 8)
ss('TownArea/TownSquare/WellRoof', 'position', {'x':30.0,'y':5.0,'z':115.0})
ss('TownArea/TownSquare/WellRoof', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.12,'b':0.08,'a':1},
    'roughness':0.8
})

print("  Town well built!")

# ============================================================
# 6. Two more town buildings (back row)
# ============================================================
print("\n=== More Buildings ===")

wood_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.22,'b':0.12,'a':1},
    'roughness':0.8,'metallic':0.0
}

roof_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.10,'a':1},
    'roughness':0.85,'metallic':0.03
}

new_buildings = [
    ('House3', 15.0, 128.0, 8.0, 8.0, 5.0),
    ('House4', 45.0, 128.0, 8.0, 8.0, 5.0),
]

for name, x, z, w, d, h in new_buildings:
    # Walls
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Walls')
    ss(f'TownArea/TownSquare/{name}_Walls', 'size', {'x':w,'y':h,'z':d})
    ss(f'TownArea/TownSquare/{name}_Walls', 'position', {'x':x,'y':h/2,'z':z})
    ss(f'TownArea/TownSquare/{name}_Walls', 'use_collision', True)
    ss(f'TownArea/TownSquare/{name}_Walls', 'material_override', wood_mat)
    
    # Door cutout
    sa(f'TownArea/TownSquare/{name}_Walls', 'CSGBox3D', f'{name}_DoorCut')
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'operation', 2)
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'size', {'x':2.0,'y':3.5,'z':1.0})
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'position', {'x':0,'y':1.75,'z':d/2})
    
    # Window cutouts
    for wi, wx in enumerate([-2.0, 2.0]):
        sa(f'TownArea/TownSquare/{name}_Walls', 'CSGBox3D', f'{name}_WinCut{wi}')
        ss(f'TownArea/TownSquare/{name}_Walls/{name}_WinCut{wi}', 'operation', 2)
        ss(f'TownArea/TownSquare/{name}_Walls/{name}_WinCut{wi}', 'size', {'x':1.5,'y':1.5,'z':1.0})
        ss(f'TownArea/TownSquare/{name}_Walls/{name}_WinCut{wi}', 'position', {'x':wx,'y':3.5,'z':d/2})
    
    # Roof
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Roof')
    ss(f'TownArea/TownSquare/{name}_Roof', 'size', {'x':w+1.0,'y':0.5,'z':d+1.0})
    ss(f'TownArea/TownSquare/{name}_Roof', 'position', {'x':x,'y':h+0.25,'z':z})
    ss(f'TownArea/TownSquare/{name}_Roof', 'material_override', roof_mat)
    
    # Roof peak
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_RoofPeak')
    ss(f'TownArea/TownSquare/{name}_RoofPeak', 'size', {'x':w+0.5,'y':2.5,'z':d+0.5})
    ss(f'TownArea/TownSquare/{name}_RoofPeak', 'position', {'x':x,'y':h+1.5,'z':z})
    ss(f'TownArea/TownSquare/{name}_RoofPeak', 'material_override', roof_mat)
    
    # Chimney
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Chimney')
    ss(f'TownArea/TownSquare/{name}_Chimney', 'size', {'x':1.0,'y':3.0,'z':1.0})
    ss(f'TownArea/TownSquare/{name}_Chimney', 'position', {'x':x+w/2-1.5,'y':h+2.0,'z':z})
    ss(f'TownArea/TownSquare/{name}_Chimney', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.30,'g':0.25,'b':0.20,'a':1},
        'roughness':0.85
    })
    
    # Window light
    sa('TownArea/TownSquare', 'OmniLight3D', f'{name}_WindowLight')
    ss(f'TownArea/TownSquare/{name}_WindowLight', 'position', {'x':x,'y':3.0,'z':z-d/2-0.5})
    ss(f'TownArea/TownSquare/{name}_WindowLight', 'light_color', {'r':1.0,'g':0.8,'b':0.4,'a':1})
    ss(f'TownArea/TownSquare/{name}_WindowLight', 'light_energy', 1.0)
    ss(f'TownArea/TownSquare/{name}_WindowLight', 'omni_range', 8.0)

print("  2 more buildings added!")

# ============================================================
# 7. Flower patches and grass tufts
# ============================================================
print("\n=== Vegetation Details ===")

flower_colors = [
    {'r':0.8,'g':0.2,'b':0.2,'a':1},  # red
    {'r':0.9,'g':0.8,'b':0.2,'a':1},  # yellow
    {'r':0.6,'g':0.3,'b':0.8,'a':1},  # purple
    {'r':0.9,'g':0.9,'b':0.9,'a':1},  # white
]

flower_positions = [
    (5, 48), (7, 52), (10, 56), (22, 70), (28, 74),
    (35, 80), (12, 108), (48, 108), (12, 122), (48, 122),
    (20, 130), (40, 130),
]

for i, (x, z) in enumerate(flower_positions):
    color = flower_colors[i % len(flower_colors)]
    name = f'Flower{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.3)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.3,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':color,
        'roughness':0.9,'metallic':0.0,
        'emission_enabled':True,
        'emission':color,
        'emission_energy_multiplier':0.3
    })

# Grass tufts
grass_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.18,'b':0.05,'a':1},
    'roughness':0.95,'metallic':0.0
}

for i in range(20):
    import random
    random.seed(i + 100)
    x = random.uniform(-20, 60)
    z = random.uniform(40, 135)
    name = f'GrassTuft{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.5)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.25,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', grass_mat)

print(f"  {len(flower_positions)} flower patches, 20 grass tufts!")

# ============================================================
# Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 8 complete!")
