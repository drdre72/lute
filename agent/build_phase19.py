#!/usr/bin/env python3
"""Phase 19: Rust-game-inspired props — rusted barrels, scrap metal piles,
chain link fence, metal debris, oil drums, wrecked cart, weathered signs."""
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

# Rust material palette
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
OIL_STONE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.07,'b':0.06,'a':1},
    'roughness':0.5,'metallic':0.1
}
MOSS_STONE = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.22,'b':0.15,'a':1},
    'roughness':0.9
}

# ============================================================
# 1. Rusted oil drums near blacksmith
# ============================================================
print("=== Oil Drums ===")

for i, (x, z) in enumerate([(34, 125), (35, 126), (34.5, 127)]):
    name = f'OilDrum{i}'
    sa('TownArea/TownSquare', 'CSGCylinder3D', name)
    ss(f'TownArea/TownSquare/{name}', 'radius', 0.6)
    ss(f'TownArea/TownSquare/{name}', 'height', 1.5)
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.75,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', RUST_METAL)
    ss(f'TownArea/TownSquare/{name}', 'use_collision', True)

# Drum rings (detail bands)
for i, (x, z) in enumerate([(34, 125), (35, 126), (34.5, 127)]):
    for h in [0.3, 1.2]:
        name = f'DrumRing{i}_{h}'
        sa('TownArea/TownSquare', 'CSGCylinder3D', name)
        ss(f'TownArea/TownSquare/{name}', 'radius', 0.62)
        ss(f'TownArea/TownSquare/{name}', 'height', 0.08)
        ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':h,'z':z})
        ss(f'TownArea/TownSquare/{name}', 'material_override', HEAVY_RUST)

print("  3 rusted oil drums!")

# ============================================================
# 2. Scrap metal pile near training grounds
# ============================================================
print("\n=== Scrap Metal Pile ===")

random.seed(123)
for i in range(8):
    x = -26.0 + random.uniform(-2, 2)
    z = 116.0 + random.uniform(-2, 2)
    y = random.uniform(0.1, 0.8)
    name = f'Scrap{i:02d}'
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', name)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'size', {
        'x':random.uniform(0.3, 1.0),
        'y':random.uniform(0.1, 0.4),
        'z':random.uniform(0.3, 1.0)
    })
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'rotation_degrees', {
        'x':random.uniform(-20,20),
        'y':random.uniform(0,360),
        'z':random.uniform(-20,20)
    })
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'material_override', 
        RUST_METAL if i % 2 == 0 else HEAVY_RUST)

print("  Scrap metal pile!")

# ============================================================
# 3. Rusted barrels with rings (near market)
# ============================================================
print("\n=== Rusted Barrels ===")

for i, (x, z) in enumerate([(6, 108), (7, 109), (53, 122)]):
    name = f'RustBarrel{i}'
    sa('TownArea/TownSquare', 'CSGCylinder3D', name)
    ss(f'TownArea/TownSquare/{name}', 'radius', 0.5)
    ss(f'TownArea/TownSquare/{name}', 'height', 1.2)
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.6,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', HEAVY_RUST)
    ss(f'TownArea/TownSquare/{name}', 'use_collision', True)
    
    # Barrel rings
    for h in [0.2, 1.0]:
        sa('TownArea/TownSquare', 'CSGCylinder3D', f'{name}_Ring{h}')
        ss(f'TownArea/TownSquare/{name}_Ring{h}', 'radius', 0.52)
        ss(f'TownArea/TownSquare/{name}_Ring{h}', 'height', 0.06)
        ss(f'TownArea/TownSquare/{name}_Ring{h}', 'position', {'x':x,'y':h,'z':z})
        ss(f'TownArea/TownSquare/{name}_Ring{h}', 'material_override', RUST_METAL)

print("  3 rusted barrels!")

# ============================================================
# 4. Chain link fence section near training grounds
# ============================================================
print("\n=== Chain Link Fence ===")

# Fence posts
for i in range(4):
    x = -12.0 + i * 3.0
    name = f'FencePost{i}'
    sa('TownArea/TrainingGround/TrainingGeometry', 'CSGCylinder3D', name)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'radius', 0.08)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'height', 3.0)
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'position', {'x':x,'y':1.5,'z':127.0})
    ss(f'TownArea/TrainingGround/TrainingGeometry/{name}', 'material_override', HEAVY_RUST)

# Fence mesh (translucent dark)
sa('TownArea/TrainingGround/TrainingGeometry', 'CSGBox3D', 'FenceMesh')
ss('TownArea/TrainingGround/TrainingGeometry/FenceMesh', 'size', {'x':9.0,'y':2.5,'z':0.05})
ss('TownArea/TrainingGround/TrainingGeometry/FenceMesh', 'position', {'x':-7.5,'y':1.25,'z':127.0})
ss('TownArea/TrainingGround/TrainingGeometry/FenceMesh', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.05,'b':0.05,'a':0.4},
    'roughness':0.9,'metallic':0.5,
    'transparency':2
})

print("  Chain link fence section!")

# ============================================================
# 5. Wrecked/broken cart near forest path
# ============================================================
print("\n=== Wrecked Cart ===")

# Tilted cart body
sa('TownArea/Terrain', 'CSGBox3D', 'WreckedCart')
ss('TownArea/Terrain/WreckedCart', 'size', {'x':3.0,'y':1.0,'z':1.5})
ss('TownArea/Terrain/WreckedCart', 'position', {'x':18.0,'y':0.8,'z':148.0})
ss('TownArea/Terrain/WreckedCart', 'rotation_degrees', {'x':0,'y':25,'z':15})
ss('TownArea/Terrain/WreckedCart', 'material_override', GRUNGY_WOOD)

# Broken wheel
sa('TownArea/Terrain', 'CSGCylinder3D', 'WreckedWheel')
ss('TownArea/Terrain/WreckedWheel', 'radius', 0.6)
ss('TownArea/Terrain/WreckedWheel', 'height', 0.15)
ss('TownArea/Terrain/WreckedWheel', 'position', {'x':16.0,'y':0.3,'z':150.0})
ss('TownArea/Terrain/WreckedWheel', 'rotation_degrees', {'x':0,'y':0,'z':80})
ss('TownArea/Terrain/WreckedWheel', 'material_override', HEAVY_RUST)

# Scattered planks
for i in range(3):
    name = f'WreckedPlank{i}'
    sa('TownArea/Terrain', 'CSGBox3D', name)
    ss(f'TownArea/Terrain/{name}', 'size', {'x':1.5,'y':0.1,'z':0.3})
    ss(f'TownArea/Terrain/{name}', 'position', {
        'x':18.0 + random.uniform(-1, 1),
        'y':0.1,
        'z':149.0 + random.uniform(-1, 1)
    })
    ss(f'TownArea/Terrain/{name}', 'rotation_degrees', {
        'x':0,
        'y':random.uniform(0, 360),
        'z':random.uniform(-10, 10)
    })
    ss(f'TownArea/Terrain/{name}', 'material_override', GRUNGY_WOOD)

print("  Wrecked cart with debris!")

# ============================================================
# 6. Rusted metal signs along path
# ============================================================
print("\n=== Rusted Signs ===")

sign_positions = [
    (28, 60, 0),    # Path to town
    (28, 100, 0),   # Town entrance
    (28, 145, 45),  # Forest path
]

for i, (x, z, rot) in enumerate(sign_positions):
    name = f'RustSign{i}'
    # Post
    sa('TownArea/Terrain', 'CSGCylinder3D', f'{name}_Post')
    ss(f'TownArea/Terrain/{name}_Post', 'radius', 0.08)
    ss(f'TownArea/Terrain/{name}_Post', 'height', 4.0)
    ss(f'TownArea/Terrain/{name}_Post', 'position', {'x':x,'y':2.0,'z':z})
    ss(f'TownArea/Terrain/{name}_Post', 'material_override', HEAVY_RUST)
    
    # Sign board
    sa('TownArea/Terrain', 'CSGBox3D', f'{name}_Board')
    ss(f'TownArea/Terrain/{name}_Board', 'size', {'x':1.5,'y':1.0,'z':0.08})
    ss(f'TownArea/Terrain/{name}_Board', 'position', {'x':x,'y':3.0,'z':z})
    ss(f'TownArea/Terrain/{name}_Board', 'rotation_degrees', {'x':0,'y':rot,'z':0})
    ss(f'TownArea/Terrain/{name}_Board', 'material_override', RUST_METAL)

print("  3 rusted signs placed!")

# ============================================================
# 7. Oil stain patches on ground
# ============================================================
print("\n=== Oil Stains ===")

stain_positions = [
    (36, 125),  # Near blacksmith
    (40, 113),  # Near cart
    (28, 155),  # Near campfire
    (20, 172),  # Near dock
]

for i, (x, z) in enumerate(stain_positions):
    name = f'OilStain{i}'
    sa('TownArea/Terrain', 'CSGCylinder3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', random.uniform(1.5, 2.5))
    ss(f'TownArea/Terrain/{name}', 'height', 0.02)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':0.02,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', OIL_STONE)

print("  4 oil stain patches!")

# ============================================================
# 8. Rusted pipe sections near blacksmith
# ============================================================
print("\n=== Rusted Pipes ===")

for i in range(3):
    x = 33.0 + i * 0.5
    z = 126.0
    name = f'Pipe{i}'
    sa('TownArea/TownSquare', 'CSGCylinder3D', name)
    ss(f'TownArea/TownSquare/{name}', 'radius', 0.15)
    ss(f'TownArea/TownSquare/{name}', 'height', 2.0)
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.1,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'rotation_degrees', {'x':90,'y':0,'z':0})
    ss(f'TownArea/TownSquare/{name}', 'material_override', HEAVY_RUST)

print("  3 rusted pipe sections!")

# ============================================================
# 9. Moss patches on stone paths
# ============================================================
print("\n=== Moss Patches ===")

moss_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.20,'b':0.08,'a':1},
    'roughness':0.95
}

random.seed(222)
for i in range(10):
    x = random.uniform(25, 35)
    z = random.uniform(100, 125)
    name = f'MossPatch{i:02d}'
    sa('TownArea/TownSquare', 'CSGSphere3D', name)
    ss(f'TownArea/TownSquare/{name}', 'radius', random.uniform(0.3, 0.6))
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.05,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', moss_mat)

print("  10 moss patches on paths!")

# ============================================================
# 10. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 19 complete — Rust-style props added!")
