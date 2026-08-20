#!/usr/bin/env python3
"""Phase 13: Waterfall from mountains, hidden grove with fairy ring,
stone bridge repair markers, more town props, bird statues."""
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
# 1. Waterfall from mountains into lake
# ============================================================
print("=== Waterfall ===")

# Cliff face
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'WaterfallCliff')
ss('TownArea/LakeRegion/LakeGeometry/WaterfallCliff', 'size', {'x':15.0,'y':30.0,'z':3.0})
ss('TownArea/LakeRegion/LakeGeometry/WaterfallCliff', 'position', {'x':30.0,'y':15.0,'z':190.0})
ss('TownArea/LakeRegion/LakeGeometry/WaterfallCliff', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.14,'b':0.12,'a':1},
    'roughness':0.9
})

# Waterfall (translucent blue slab)
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'Waterfall')
ss('TownArea/LakeRegion/LakeGeometry/Waterfall', 'size', {'x':6.0,'y':28.0,'z':1.0})
ss('TownArea/LakeRegion/LakeGeometry/Waterfall', 'position', {'x':30.0,'y':14.0,'z':188.0})
ss('TownArea/LakeRegion/LakeGeometry/Waterfall', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.3,'g':0.5,'b':0.7,'a':0.4},
    'roughness':0.05,'metallic':0.0,
    'transparency':2,
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.2,'b':0.4,'a':1},
    'emission_energy_multiplier':0.5
})

# Mist at waterfall base
sa('TownArea/LakeRegion', 'OmniLight3D', 'WaterfallMist')
ss('TownArea/LakeRegion/WaterfallMist', 'position', {'x':30.0,'y':2.0,'z':186.0})
ss('TownArea/LakeRegion/WaterfallMist', 'light_color', {'r':0.3,'g':0.4,'b':0.5,'a':1})
ss('TownArea/LakeRegion/WaterfallMist', 'light_energy', 1.5)
ss('TownArea/LakeRegion/WaterfallMist', 'omni_range', 15.0)

# Splash rocks
for i in range(5):
    x = 28.0 + i * 1.0
    name = f'SplashRock{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':1.0,'y':0.8,'z':1.0})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':x,'y':0.4,'z':185.0})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.20,'g':0.18,'b':0.16,'a':1},
        'roughness':0.9
    })

print("  Waterfall built!")

# ============================================================
# 2. Hidden grove with fairy ring (mushroom circle)
# ============================================================
print("\n=== Hidden Grove ===")

add('TownArea', 'Node3D', 'HiddenGrove')
add('TownArea/HiddenGrove', 'CSGCombiner3D', 'GroveGeometry')

# Grove clearing ground
sa('TownArea/HiddenGrove/GroveGeometry', 'CSGBox3D', 'GroveFloor')
ss('TownArea/HiddenGrove/GroveGeometry/GroveFloor', 'size', {'x':15.0,'y':0.2,'z':15.0})
ss('TownArea/HiddenGrove/GroveGeometry/GroveFloor', 'position', {'x':-30.0,'y':0.1,'z':100.0})
ss('TownArea/HiddenGrove/GroveGeometry/GroveFloor', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.06,'g':0.12,'b':0.05,'a':1},
    'roughness':0.95,
    'uv1_scale':{'x':5,'y':5,'z':5}
})

# Fairy ring mushrooms
mushroom_cap_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.7,'g':0.2,'b':0.2,'a':1},
    'roughness':0.8,
    'emission_enabled':True,
    'emission':{'r':0.3,'g':0.05,'b':0.05,'a':1},
    'emission_energy_multiplier':0.5
}

mushroom_stem_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.9,'g':0.88,'b':0.8,'a':1},
    'roughness':0.8
}

for i in range(12):
    angle = i * math.pi / 6
    x = -30.0 + math.cos(angle) * 4.0
    z = 100.0 + math.sin(angle) * 4.0
    name = f'Mushroom{i:02d}'
    
    # Stem
    sa('TownArea/HiddenGrove/GroveGeometry', 'CSGCylinder3D', f'{name}_Stem')
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Stem', 'radius', 0.15)
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Stem', 'height', 0.8)
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Stem', 'position', {'x':x,'y':0.4,'z':z})
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Stem', 'material_override', mushroom_stem_mat)
    
    # Cap
    sa('TownArea/HiddenGrove/GroveGeometry', 'CSGSphere3D', f'{name}_Cap')
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Cap', 'radius', 0.4)
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Cap', 'position', {'x':x,'y':0.9,'z':z})
    ss(f'TownArea/HiddenGrove/GroveGeometry/{name}_Cap', 'material_override', mushroom_cap_mat)

# Central glow
sa('TownArea/HiddenGrove', 'OmniLight3D', 'FairyGlow')
ss('TownArea/HiddenGrove/FairyGlow', 'position', {'x':-30.0,'y':1.5,'z':100.0})
ss('TownArea/HiddenGrove/FairyGlow', 'light_color', {'r':0.3,'g':0.1,'b':0.1,'a':1})
ss('TownArea/HiddenGrove/FairyGlow', 'light_energy', 1.5)
ss('TownArea/HiddenGrove/FairyGlow', 'omni_range', 10.0)

# Grove trees (encircling)
grove_tree_trunk = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.10,'b':0.05,'a':1},
    'roughness':0.9
}
grove_tree_leaf = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.04,'g':0.15,'b':0.04,'a':1},
    'roughness':0.95
}

for i in range(10):
    angle = i * math.pi / 5
    x = -30.0 + math.cos(angle) * 8.0
    z = 100.0 + math.sin(angle) * 8.0
    idx = 56 + i
    
    sa('TownArea/HiddenGrove/GroveGeometry', 'CSGCylinder3D', f'GroveTrunk{idx:02d}')
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveTrunk{idx:02d}', 'radius', 0.5)
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveTrunk{idx:02d}', 'height', 7.0)
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveTrunk{idx:02d}', 'position', {'x':x,'y':3.5,'z':z})
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveTrunk{idx:02d}', 'material_override', grove_tree_trunk)
    
    sa('TownArea/HiddenGrove/GroveGeometry', 'CSGSphere3D', f'GroveLeaves{idx:02d}')
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveLeaves{idx:02d}', 'radius', 4.0)
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveLeaves{idx:02d}', 'position', {'x':x,'y':8.5,'z':z})
    ss(f'TownArea/HiddenGrove/GroveGeometry/GroveLeaves{idx:02d}', 'material_override', grove_tree_leaf)

print("  Hidden grove with fairy ring built!")

# ============================================================
# 3. Bird statues on temple exterior
# ============================================================
print("\n=== Bird Statues ===")

statue_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.18,'g':0.16,'b':0.14,'a':1},
    'roughness':0.7,'metallic':0.1
}

for side, x in [('L', -9.0), ('R', 9.0)]:
    name = f'BirdStatue_{side}'
    # Body
    sa('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', f'{name}_Body')
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Body', 'size', {'x':0.8,'y':1.5,'z':1.5})
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Body', 'position', {'x':x,'y':19.0,'z':20.0})
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Body', 'material_override', statue_mat)
    
    # Head
    sa('Architecture/Exterior/ExteriorPlaza', 'CSGSphere3D', f'{name}_Head')
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Head', 'radius', 0.5)
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Head', 'position', {'x':x,'y':20.2,'z':20.0})
    ss(f'Architecture/Exterior/ExteriorPlaza/{name}_Head', 'material_override', statue_mat)

print("  2 bird statues placed!")

# ============================================================
# 4. Town props — hay bales, sacks, lantern posts
# ============================================================
print("\n=== Town Props ===")

# Hay bale near shop
sa('TownArea/TownSquare', 'CSGCylinder3D', 'HayBale')
ss('TownArea/TownSquare/HayBale', 'radius', 1.0)
ss('TownArea/TownSquare/HayBale', 'height', 1.5)
ss('TownArea/TownSquare/HayBale', 'rotation_degrees', {'x':90,'y':0,'z':0})
ss('TownArea/TownSquare/HayBale', 'position', {'x':8.0,'y':0.75,'z':104.0})
ss('TownArea/TownSquare/HayBale', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.6,'g':0.5,'b':0.2,'a':1},
    'roughness':0.95
})

# Sacks near market stalls
for i, (x, z) in enumerate([(21, 109), (23, 109)]):
    name = f'Sack{i}'
    sa('TownArea/TownSquare', 'CSGSphere3D', name)
    ss(f'TownArea/TownSquare/{name}', 'radius', 0.6)
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.6,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.35,'g':0.28,'b':0.15,'a':1},
        'roughness':0.9
    })

# Lantern posts at town gate entrance
for side, x in [('L', -3.5), ('R', 3.5)]:
    name = f'GateLantern_{side}'
    sa('TownArea/TownGate', 'CSGCylinder3D', f'{name}_Post')
    ss(f'TownArea/TownGate/{name}_Post', 'radius', 0.12)
    ss(f'TownArea/TownGate/{name}_Post', 'height', 5.0)
    ss(f'TownArea/TownGate/{name}_Post', 'position', {'x':x,'y':2.5,'z':92.0})
    ss(f'TownArea/TownGate/{name}_Post', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.12,'g':0.10,'b':0.08,'a':1},
        'roughness':0.4,'metallic':0.5
    })
    
    # Lantern body
    sa('TownArea/TownGate', 'CSGBox3D', f'{name}_Body')
    ss(f'TownArea/TownGate/{name}_Body', 'size', {'x':0.5,'y':0.8,'z':0.5})
    ss(f'TownArea/TownGate/{name}_Body', 'position', {'x':x,'y':5.4,'z':92.0})
    ss(f'TownArea/TownGate/{name}_Body', 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':{'r':0.3,'g':0.2,'b':0.05,'a':1},
        'emission_enabled':True,
        'emission':{'r':0.8,'g':0.5,'b':0.1,'a':1},
        'emission_energy_multiplier':2.0,
        'roughness':0.4
    })

print("  Town props added!")

# ============================================================
# 5. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 13 complete!")
