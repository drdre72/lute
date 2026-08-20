#!/usr/bin/env python3
"""Phase 7: Fix stall legs, add more town detail, bridge over path stream,
distant mountains, and final environment polish."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    print(f"  add {name}: {r.get('ok')}")
    return r

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    print(f"  set {path.split('/')[-1]}.{prop}: {r.get('ok')}")
    return r

# ============================================================
# 1. Fix stall legs — use clean names
# ============================================================
print("=== Fix Stall Legs ===")

stalls = [
    (0, 24, 107), (1, 28, 107), (2, 32, 107), (3, 36, 107),
]

for idx, x, z in stalls:
    for li, (lx, lz) in enumerate([(-1.3, -0.8), (-1.3, 0.8), (1.3, -0.8), (1.3, 0.8)]):
        name = f'Stall{idx}_Leg{li}'
        add('TownArea/TownSquare', 'CSGBox3D', name)
        setprop(f'TownArea/TownSquare/{name}', 'size', {'x':0.15,'y':1.4,'z':0.15})
        setprop(f'TownArea/TownSquare/{name}', 'position', {'x':x+lx,'y':0.7,'z':z+lz})
    
    for pi, px in enumerate([-1.5, 1.5]):
        name = f'Stall{idx}_Pole{pi}'
        add('TownArea/TownSquare', 'CSGBox3D', name)
        setprop(f'TownArea/TownSquare/{name}', 'size', {'x':0.1,'y':3.0,'z':0.1})
        setprop(f'TownArea/TownSquare/{name}', 'position', {'x':x+px,'y':1.5,'z':z})

print("  Stall legs fixed!")

# ============================================================
# 2. Stream + bridge along the path
# ============================================================
print("\n=== Stream & Bridge ===")

# Stream (shallow water channel crossing the path at z~65)
add('TownArea/Terrain', 'CSGBox3D', 'Stream')
setprop('TownArea/Terrain/Stream', 'size', {'x':60.0,'y':0.3,'z':4.0})
setprop('TownArea/Terrain/Stream', 'position', {'x':15.0,'y':0.05,'z':65.0})
setprop('TownArea/Terrain/Stream', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.20,'b':0.35,'a':0.7},
    'roughness':0.1,'metallic':0.0,
    'transparency':2,
    'emission_enabled':True,
    'emission':{'r':0.03,'g':0.10,'b':0.20,'a':1},
    'emission_energy_multiplier':0.3
})

# Stone bridge over stream
add('TownArea/Terrain', 'CSGBox3D', 'Bridge')
setprop('TownArea/Terrain/Bridge', 'size', {'x':8.0,'y':0.5,'z':6.0})
setprop('TownArea/Terrain/Bridge', 'position', {'x':0,'y':0.3,'z':65.0})
setprop('TownArea/Terrain/Bridge', 'use_collision', True)
setprop('TownArea/Terrain/Bridge', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.27,'b':0.23,'a':1},
    'roughness':0.8,'metallic':0.05,
    'uv1_scale':{'x':2,'y':1,'z':2}
})

# Bridge railings
for side, x in [('L', -3.5), ('R', 3.5)]:
    add('TownArea/Terrain', 'CSGBox3D', f'BridgeRail_{side}')
    setprop(f'TownArea/Terrain/BridgeRail_{side}', 'size', {'x':0.3,'y':1.0,'z':6.0})
    setprop(f'TownArea/Terrain/BridgeRail_{side}', 'position', {'x':x,'y':1.0,'z':65.0})
    setprop(f'TownArea/Terrain/BridgeRail_{side}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.25,'g':0.22,'b':0.19,'a':1},
        'roughness':0.6,'metallic':0.15
    })

print("  Stream & bridge done!")

# ============================================================
# 3. Distant mountains (large CSG shapes on the horizon)
# ============================================================
print("\n=== Distant Mountains ===")

mountain_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.08,'g':0.09,'b':0.12,'a':1},
    'roughness':0.95,'metallic':0.0
}

mountain_positions = [
    (-60, 140, 25, 30), (-40, 150, 20, 25), (-20, 155, 30, 35),
    (20, 155, 25, 30), (50, 150, 22, 28), (70, 140, 28, 32),
    (-70, 80, 20, 25), (70, 80, 22, 28),
]

for i, (x, z, h, r) in enumerate(mountain_positions):
    name = f'Mountain{i:02d}'
    add('TownArea/Terrain', 'CSGCone3D', name)
    setprop(f'TownArea/Terrain/{name}', 'radius', r)
    setprop(f'TownArea/Terrain/{name}', 'height', h)
    setprop(f'TownArea/Terrain/{name}', 'sides', 5)
    setprop(f'TownArea/Terrain/{name}', 'position', {'x':x,'y':h/2,'z':z})
    setprop(f'TownArea/Terrain/{name}', 'material_override', mountain_mat)

print(f"  {len(mountain_positions)} mountains placed!")

# ============================================================
# 4. More trees around town perimeter
# ============================================================
print("\n=== Perimeter Trees ===")

perimeter_trees = [
    (-15, 100), (-15, 110), (-15, 120), (-15, 130),
    (60, 100), (60, 110), (60, 120), (60, 130),
    (10, 140), (20, 140), (40, 140), (50, 140),
    (-10, 50), (-15, 55), (55, 50), (60, 55),
]

tree_trunk_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.14,'b':0.08,'a':1},
    'roughness':0.9,'metallic':0.0
}

tree_leaf_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.22,'b':0.07,'a':1},
    'roughness':0.95,'metallic':0.0
}

for i, (x, z) in enumerate(perimeter_trees):
    idx = 15 + i  # continue from existing tree numbering
    add('TownArea/Terrain', 'CSGCylinder3D', f'TreeTrunk{idx:02d}')
    setprop(f'TownArea/Terrain/TreeTrunk{idx:02d}', 'radius', 0.4)
    setprop(f'TownArea/Terrain/TreeTrunk{idx:02d}', 'height', 5.0)
    setprop(f'TownArea/Terrain/TreeTrunk{idx:02d}', 'position', {'x':x,'y':2.5,'z':z})
    setprop(f'TownArea/Terrain/TreeTrunk{idx:02d}', 'material_override', tree_trunk_mat)
    
    add('TownArea/Terrain', 'CSGSphere3D', f'TreeLeaves{idx:02d}')
    setprop(f'TownArea/Terrain/TreeLeaves{idx:02d}', 'radius', 3.0)
    setprop(f'TownArea/Terrain/TreeLeaves{idx:02d}', 'position', {'x':x,'y':6.5,'z':z})
    setprop(f'TownArea/Terrain/TreeLeaves{idx:02d}', 'material_override', tree_leaf_mat)

print(f"  {len(perimeter_trees)} perimeter trees added!")

# ============================================================
# 5. Town signpost and crates
# ============================================================
print("\n=== Town Details ===")

# Signpost at town entrance
add('TownArea/TownGate', 'CSGBox3D', 'Signpost')
setprop('TownArea/TownGate/Signpost', 'size', {'x':0.2,'y':4.0,'z':0.2})
setprop('TownArea/TownGate/Signpost', 'position', {'x':-3.0,'y':2.0,'z':93.0})
setprop('TownArea/TownGate/Signpost', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.20,'b':0.10,'a':1},
    'roughness':0.8
})

# Sign board
add('TownArea/TownGate', 'CSGBox3D', 'SignBoard')
setprop('TownArea/TownGate/SignBoard', 'size', {'x':2.5,'y':1.0,'z':0.15})
setprop('TownArea/TownGate/SignBoard', 'position', {'x':-3.0,'y':3.5,'z':93.0})
setprop('TownArea/TownGate/SignBoard', 'rotation_degrees', {'x':0,'y':20,'z':0})
setprop('TownArea/TownGate/SignBoard', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.40,'g':0.28,'b':0.15,'a':1},
    'roughness':0.7
})

# Crates near the shop
for i, (x, z) in enumerate([(10, 103), (11, 104), (9, 105)]):
    name = f'Crate{i}'
    add('TownArea/TownSquare', 'CSGBox3D', name)
    setprop(f'TownArea/TownSquare/{name}', 'size', {'x':1.2,'y':1.2,'z':1.2})
    setprop(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.6,'z':z})
    setprop(f'TownArea/TownSquare/{name}', 'rotation_degrees', {'x':0,'y':i*15,'z':0})
    setprop(f'TownArea/TownSquare/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.35,'g':0.22,'b':0.12,'a':1},
        'roughness':0.8
    })

# Barrel near the inn
add('TownArea/TownSquare', 'CSGCylinder3D', 'Barrel')
setprop('TownArea/TownSquare/Barrel', 'radius', 0.6)
setprop('TownArea/TownSquare/Barrel', 'height', 1.2)
setprop('TownArea/TownSquare/Barrel', 'position', {'x':50.0,'y':0.6,'z':103.0})
setprop('TownArea/TownSquare/Barrel', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.18,'b':0.08,'a':1},
    'roughness':0.75
})

print("  Town details added!")

# ============================================================
# 6. Final environment polish — SSAO, fog tweaks
# ============================================================
print("\n=== Final Environment ===")

setprop('WorldEnvironment', 'environment', {
    'class':'Environment',
    'background_mode':3,
    'sky':{'class':'Sky','sky_material':{'class':'GradientSky','top_color':{'r':0.01,'g':0.01,'b':0.06,'a':1},'bottom_color':{'r':0.18,'g':0.12,'b':0.20,'a':1},'curve':0.7}},
    'ambient_light_source':2,
    'ambient_light_color':{'r':0.10,'g':0.10,'b':0.16,'a':1},
    'ambient_light_energy':0.5,
    'fog_enabled':True,
    'fog_light_color':{'r':0.06,'g':0.06,'b':0.14,'a':1},
    'fog_light_energy':0.6,
    'fog_density':0.035,
    'fog_aerial_perspective':0.7,
    'glow_enabled':True,
    'glow_intensity':1.3,
    'glow_strength':1.2,
    'glow_blend_mode':1,
    'glow_bloom':0.25,
    'volumetric_fog_enabled':True,
    'volumetric_fog_density':0.008,
    'volumetric_fog_albedo':{'r':0.08,'g':0.08,'b':0.18,'a':1},
    'volumetric_fog_emission':{'r':0.03,'g':0.05,'b':0.10,'a':1},
    'volumetric_fog_emission_energy':0.2,
    'tonemap_mode':2,
    'tonemap_white':1.3,
    'ssao_enabled':True,
    'ssao_radius':1.5,
    'ssao_intensity':2.0,
    'ssao_power':1.5,
    'ssao_light_affect':0.5,
    'ssrf_enabled':True,
    'ssrf_amount':0.05
})

print("  Environment polished!")

# ============================================================
# Save & final count
# ============================================================
print("\n=== Final Save ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")

r = call_tool('get_scene_tree', {})
if r.get('ok'):
    tree = r['tree']
    print(f"\nRoot: {tree['name']} ({tree['type']})")
    print(f"Top-level: {len(tree.get('children', []))} nodes")
    for child in tree.get('children', []):
        n = len(child.get('children', []))
        print(f"  - {child['name']} ({child['type']}) [{n} children]")
        if n > 0 and child['name'] in ('Architecture', 'TownArea'):
            for sub in child.get('children', []):
                sn = len(sub.get('children', []))
                print(f"      - {sub['name']} ({sub['type']}) [{sn} children]")
                if sn > 0 and sub['name'] in ('NaveCombiner', 'Terrain', 'TownSquare'):
                    for ssub in sub.get('children', []):
                        ssn = len(sub.get('children', []))
                        if ssn > 20:
                            print(f"          - {sub['name']} has {sn} children")

print("\nPhase 7 complete!")
