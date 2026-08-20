#!/usr/bin/env python3
"""Phase 11: More town life — market goods on stalls, town banner flags,
stone bench seats, cart, more building detail, fireflies."""
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
# 1. Market goods on stall tables
# ============================================================
print("=== Market Goods ===")

goods_colors = [
    {'r':0.8,'g':0.2,'b':0.1,'a':1},  # apple red
    {'r':0.9,'g':0.7,'b':0.2,'a':1},  # bread golden
    {'r':0.6,'g':0.3,'b':0.1,'a':1},  # pottery brown
    {'r':0.2,'g':0.5,'b':0.2,'a':1},  # vegetable green
]

stall_xs = [24, 28, 32, 36]
for si, sx in enumerate(stall_xs):
    sz = 107
    for gi in range(4):
        gx = sx - 1.0 + gi * 0.7
        gz = sz - 0.5 + (gi % 2) * 0.5
        color = goods_colors[(si + gi) % len(goods_colors)]
        name = f'Good_{si}_{gi}'
        sa('TownArea/TownSquare', 'CSGSphere3D', name)
        ss(f'TownArea/TownSquare/{name}', 'radius', 0.2)
        ss(f'TownArea/TownSquare/{name}', 'position', {'x':gx,'y':1.7,'z':gz})
        ss(f'TownArea/TownSquare/{name}', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':color,
            'roughness':0.7,'metallic':0.0
        })

print("  Market goods placed!")

# ============================================================
# 2. Town banner flags on walls
# ============================================================
print("\n=== Town Banners ===")

flag_colors = [
    {'r':0.6,'g':0.1,'b':0.1,'a':1},
    {'r':0.1,'g':0.2,'b':0.5,'a':1},
    {'r':0.3,'g':0.2,'b':0.5,'a':1},
    {'r':0.1,'g':0.4,'b':0.2,'a':1},
]

for i, (x, z) in enumerate([(12, 110), (52, 110), (12, 130), (52, 130)]):
    color = flag_colors[i]
    name = f'TownFlag{i}'
    # Pole
    sa('TownArea/TownSquare', 'CSGCylinder3D', f'{name}_Pole')
    ss(f'TownArea/TownSquare/{name}_Pole', 'radius', 0.1)
    ss(f'TownArea/TownSquare/{name}_Pole', 'height', 7.0)
    ss(f'TownArea/TownSquare/{name}_Pole', 'position', {'x':x,'y':3.5,'z':z})
    ss(f'TownArea/TownSquare/{name}_Pole', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.15,'g':0.12,'b':0.08,'a':1},
        'roughness':0.4,'metallic':0.5
    })
    # Flag
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Cloth')
    ss(f'TownArea/TownSquare/{name}_Cloth', 'size', {'x':0.05,'y':2.0,'z':3.0})
    ss(f'TownArea/TownSquare/{name}_Cloth', 'position', {'x':x+0.2,'y':8.0,'z':z+1.5})
    ss(f'TownArea/TownSquare/{name}_Cloth', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':color,
        'roughness':0.9,'metallic':0.0,
        'emission_enabled':True,
        'emission':color,
        'emission_energy_multiplier':0.1
    })

print("  4 town banners placed!")

# ============================================================
# 3. Stone benches around the fountain
# ============================================================
print("\n=== Stone Benches ===")

bench_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.32,'g':0.29,'b':0.25,'a':1},
    'roughness':0.8,'metallic':0.05
}

bench_positions = [
    (24, 110, 0), (36, 110, 0),
    (30, 104, 90), (30, 116, 90),
]

for i, (x, z, rot) in enumerate(bench_positions):
    name = f'Bench{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':3.0,'y':0.5,'z':1.0})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.75,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'rotation_degrees', {'x':0,'y':rot,'z':0})
    ss(f'TownArea/TownSquare/{name}', 'material_override', bench_mat)
    ss(f'TownArea/TownSquare/{name}', 'use_collision', True)

print("  4 stone benches placed!")

# ============================================================
# 4. Wooden cart near market
# ============================================================
print("\n=== Cart ===")

sa('TownArea/TownSquare', 'CSGBox3D', 'CartBase')
ss('TownArea/TownSquare/CartBase', 'size', {'x':3.0,'y':1.0,'z':1.5})
ss('TownArea/TownSquare/CartBase', 'position', {'x':40.0,'y':1.0,'z':113.0})
ss('TownArea/TownSquare/CartBase', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.18,'b':0.08,'a':1},
    'roughness':0.8
})

# Cart wheels
for side, x in [('L', 38.6), ('R', 41.4)]:
    sa('TownArea/TownSquare', 'CSGCylinder3D', f'CartWheel_{side}')
    ss(f'TownArea/TownSquare/CartWheel_{side}', 'radius', 0.6)
    ss(f'TownArea/TownSquare/CartWheel_{side}', 'height', 0.15)
    ss(f'TownArea/TownSquare/CartWheel_{side}', 'position', {'x':x,'y':0.6,'z':113.0})
    ss(f'TownArea/TownSquare/CartWheel_{side}', 'rotation_degrees', {'x':0,'y':0,'z':90})
    ss(f'TownArea/TownSquare/CartWheel_{side}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.15,'g':0.10,'b':0.05,'a':1},
        'roughness':0.7
    })

print("  Cart added!")

# ============================================================
# 5. Fireflies (tiny emissive spheres scattered around)
# ============================================================
print("\n=== Fireflies ===")

firefly_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.5,'g':0.8,'b':0.2,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.4,'g':0.8,'b':0.1,'a':1},
    'emission_energy_multiplier':5.0,
    'roughness':0.1
}

random.seed(55)
for i in range(30):
    x = random.uniform(-5, 55)
    y = random.uniform(0.5, 4.0)
    z = random.uniform(40, 135)
    name = f'Firefly{i:02d}'
    sa('TownArea/Terrain', 'CSGSphere3D', name)
    ss(f'TownArea/Terrain/{name}', 'radius', 0.08)
    ss(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':y,'z':z})
    ss(f'TownArea/Terrain/{name}', 'material_override', firefly_mat)

print(f"  30 fireflies placed!")

# ============================================================
# 6. Nave floor cracks (dark lines)
# ============================================================
print("\n=== Floor Cracks ===")

crack_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.04,'b':0.03,'a':1},
    'roughness':0.95,'metallic':0.0
}

for i in range(5):
    angle = i * 36
    name = f'FloorCrack{i}'
    sa('Architecture/NaveCombiner', 'CSGBox3D', name)
    ss(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.05,'y':0.02,'z':15.0})
    ss(f'Architecture/NaveCombiner/{name}', 'position', {'x':0,'y':0.02,'z':0})
    ss(f'Architecture/NaveCombiner/{name}', 'rotation_degrees', {'x':0,'y':angle,'z':0})
    ss(f'Architecture/NaveCombiner/{name}', 'material_override', crack_mat)

print("  Floor cracks added!")

# ============================================================
# 7. More building detail — Inn sign
# ============================================================
print("\n=== Inn Sign ===")

sa('TownArea/TownSquare', 'CSGBox3D', 'InnSign')
ss('TownArea/TownSquare/InnSign', 'size', {'x':3.0,'y':1.5,'z':0.15})
ss('TownArea/TownSquare/InnSign', 'position', {'x':45.0,'y':5.0,'z':96.0})
ss('TownArea/TownSquare/InnSign', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.20,'b':0.08,'a':1},
    'roughness':0.7,
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.05,'b':0.02,'a':1},
    'emission_energy_multiplier':0.3
})

# Sign bracket
sa('TownArea/TownSquare', 'CSGBox3D', 'InnSignBracket')
ss('TownArea/TownSquare/InnSignBracket', 'size', {'x':0.1,'y':0.1,'z':1.0})
ss('TownArea/TownSquare/InnSignBracket', 'position', {'x':45.0,'y':5.0,'z':97.0})
ss('TownArea/TownSquare/InnSignBracket', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.12,'b':0.08,'a':1},
    'roughness':0.4,'metallic':0.5
})

print("  Inn sign added!")

# ============================================================
# 8. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 11 complete!")
