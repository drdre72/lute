#!/usr/bin/env python3
"""Phase 20: Corrugated metal roofs, metal sheeting on buildings,
rusted grating, water tower, more weathering and grime layers."""
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

RUST_METAL = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.18,'b':0.08,'a':1},
    'roughness':0.85,'metallic':0.3
}
HEAVY_RUST = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.12,'b':0.05,'a':1},
    'roughness':0.95,'metallic':0.1
}
GRUNGY_WOOD = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.10,'b':0.05,'a':1},
    'roughness':0.88
}
WEATHERED_CONCRETE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.22,'g':0.21,'b':0.19,'a':1},
    'roughness':0.92
}

# ============================================================
# 1. Corrugated metal roofs on town buildings (replace roof peaks)
# ============================================================
print("=== Corrugated Metal Roofs ===")

corrugated_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.16,'b':0.07,'a':1},
    'roughness':0.75,'metallic':0.4
}

# Replace roof peaks with corrugated look
for name in ['House3', 'House4', 'Tavern', 'Blacksmith']:
    ss(f'TownArea/TownSquare/{name}_Roof', 'material_override', corrugated_mat)
    ss(f'TownArea/TownSquare/{name}_RoofPeak', 'material_override', corrugated_mat)

# Add corrugated ridge caps
for name, x, z, w in [('House3', 15, 128, 8), ('House4', 45, 128, 8), 
                       ('Tavern', 28, 128, 9), ('Blacksmith', 38, 128, 8)]:
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_RidgeCap')
    ss(f'TownArea/TownSquare/{name}_RidgeCap', 'size', {'x':w+1.0,'y':0.3,'z':0.3})
    ss(f'TownArea/TownSquare/{name}_RidgeCap', 'position', {'x':x,'y':7.5,'z':z})
    ss(f'TownArea/TownSquare/{name}_RidgeCap', 'material_override', HEAVY_RUST)

print("  Corrugated metal roofs applied!")

# ============================================================
# 2. Metal sheeting on blacksmith forge area
# ============================================================
print("\n=== Blacksmith Metal Cladding ===")

# Metal sheets on blacksmith walls (overlay)
for i, x_off in enumerate([-3.5, 3.5]):
    name = f'SmithClad{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':0.1,'y':5.5,'z':7.0})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':38.0+x_off,'y':2.75,'z':128.0})
    ss(f'TownArea/TownSquare/{name}', 'material_override', HEAVY_RUST)

# Forge chimney → rusted metal
ss('TownArea/TownSquare/Blacksmith_Chimney', 'material_override', RUST_METAL)

print("  Blacksmith metal cladded!")

# ============================================================
# 3. Water tower near training grounds
# ============================================================
print("\n=== Water Tower ===")

# Support legs (4 rusted posts)
for i in range(4):
    angle = i * math.pi / 2 + math.pi / 4
    x = -20.0 + math.cos(angle) * 2.0
    z = 113.0 + math.sin(angle) * 2.0
    name = f'WaterTowerLeg{i}'
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', name)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'size', {'x':0.3,'y':8.0,'z':0.3})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'position', {'x':x,'y':4.0,'z':z})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'material_override', HEAVY_RUST)

# Tank
sa('TownArea/TrainingGround/TrainingGeometry', 'CSGCylinder3D', 'WaterTowerTank')
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerTank', 'radius', 2.5)
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerTank', 'height', 3.0)
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerTank', 'position', {'x':-20.0,'y':9.5,'z':113.0})
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerTank', 'material_override', RUST_METAL)

# Tank rings
for h in [8.5, 10.5]:
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGCylinder3D', f'WaterTowerRing_{h}')
    ss(f'TownArea/TrainingGround/TrainingGeometry/WaterTowerRing_{h}', 'radius', 2.55)
    ss(f'TownArea/TrainingGround/TrainingGeometry/WaterTowerRing_{h}', 'height', 0.15)
    ss(f'TownArea/TrainingGround/TrainingGeometry/WaterTowerRing_{h}', 'position', {'x':-20.0,'y':h,'z':113.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/WaterTowerRing_{h}', 'material_override', HEAVY_RUST)

# Tank lid
sa('TownArea/TrainingGround/TrainingGeometry', 'CSGCylinder3D', 'WaterTowerLid')
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerLid', 'radius', 2.6)
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerLid', 'height', 0.3)
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerLid', 'position', {'x':-20.0,'y':11.2,'z':113.0})
ss('TownArea/TrainingGround/TrainingGeometry/WaterTowerLid', 'material_override', HEAVY_RUST)

# Cross bracing
for i in range(2):
    name = f'WaterTowerBrace{i}'
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', name)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'size', {'x':0.1,'y':0.1,'z':5.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'position', {'x':-20.0,'y':4.0,'z':113.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'rotation_degrees', {'x':0,'y':i*90,'z':45})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'material_override', HEAVY_RUST)

print("  Water tower built!")

# ============================================================
# 4. Rusted grating on ground (drain covers)
# ============================================================
print("\n=== Drain Gratings ===")

grating_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.08,'b':0.06,'a':1},
    'roughness':0.6,'metallic':0.5
}

for i, (x, z) in enumerate([(30, 105), (30, 120), (20, 115)]):
    name = f'DrainGrate{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':1.5,'y':0.05,'z':1.5})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.03,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', grating_mat)
    
    # Grate bars
    for j in range(4):
        sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Bar{j}')
        ss(f'TownArea/TownSquare/{name}_Bar{j}', 'size', {'x':1.4,'y':0.08,'z':0.08})
        ss(f'TownArea/TownSquare/{name}_Bar{j}', 'position', {'x':x,'y':0.05,'z':z-0.5+j*0.35})
        ss(f'TownArea/TownSquare/{name}_Bar{j}', 'material_override', grating_mat)

print("  3 drain gratings!")

# ============================================================
# 5. Rusted metal door on tomb entrance (graveyard)
# ============================================================
print("\n=== Tomb Door ===")

# Metal gate at graveyard entrance
sa('TownArea/Graveyard/GraveyardTerrain', 'CSGBox3D', 'TombGate')
ss('TownArea/Graveyard/GraveyardTerrain/TombGate', 'size', {'x':3.0,'y':3.5,'z':0.2})
ss('TownArea/Graveyard/GraveyardTerrain/TombGate', 'position', {'x':30.0,'y':1.75,'z':140.0})
ss('TownArea/Graveyard/GraveyardTerrain/TombGate', 'material_override', HEAVY_RUST)

# Gate bars
for i in range(5):
    sa('TownArea/Graveyard/GraveyardTerrain', 'CSGBox3D', f'TombBar{i}')
    ss(f'TownArea/Graveyard/GraveyardTerrain/TombBar{i}', 'size', {'x':0.15,'y':3.0,'z':0.15})
    ss(f'TownArea/Graveyard/GraveyardTerrain/TombBar{i}', 'position', {'x':28.5+i*0.7,'y':1.5,'z':140.0})
    ss(f'TownArea/Graveyard/GraveyardTerrain/TombBar{i}', 'material_override', RUST_METAL)

print("  Tomb gate with rusted bars!")

# ============================================================
# 6. Metal debris near crystal cave
# ============================================================
print("\n=== Cave Debris ===")

random.seed(666)
for i in range(5):
    x = 48.0 + random.uniform(-3, 3)
    z = 162.0 + random.uniform(-3, 3)
    y = random.uniform(0.1, 0.5)
    name = f'CaveDebris{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {
        'x':random.uniform(0.2, 0.6),
        'y':random.uniform(0.1, 0.3),
        'z':random.uniform(0.2, 0.6)
    })
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'rotation_degrees', {
        'x':random.uniform(-30,30),
        'y':random.uniform(0,360),
        'z':random.uniform(-30,30)
    })
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', HEAVY_RUST)

print("  Cave debris scattered!")

# ============================================================
# 7. Rusted lantern hooks on buildings
# ============================================================
print("\n=== Building Lantern Hooks ===")

hook_positions = [
    (15, 128, 6), (45, 128, 6), (28, 128, 6), (38, 128, 6),
]

for i, (x, z, y) in enumerate(hook_positions):
    name = f'BldgLantern{i}'
    # Hook
    sa('TownArea/TownSquare', 'CSGCylinder3D', f'{name}_Hook')
    ss(f'TownArea/TownSquare/{name}_Hook', 'radius', 0.05)
    ss(f'TownArea/TownSquare/{name}_Hook', 'height', 0.5)
    ss(f'TownArea/TownSquare/{name}_Hook', 'position', {'x':x,'y':y,'z':z-4.5})
    ss(f'TownArea/TownSquare/{name}_Hook', 'material_override', HEAVY_RUST)
    
    # Lantern body
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Body')
    ss(f'TownArea/TownSquare/{name}_Body', 'size', {'x':0.4,'y':0.6,'z':0.4})
    ss(f'TownArea/TownSquare/{name}_Body', 'position', {'x':x,'y':y-0.5,'z':z-4.5})
    ss(f'TownArea/TownSquare/{name}_Body', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.2,'g':0.12,'b':0.05,'a':1},
        'emission_enabled':True,
        'emission':{'r':0.6,'g':0.4,'b':0.1,'a':1},
        'emission_energy_multiplier':1.5,
        'roughness':0.5
    })

print("  4 building lanterns with rusted hooks!")

# ============================================================
# 8. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 20 complete — Rust-style metal infrastructure added!")
