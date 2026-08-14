#!/usr/bin/env python3
"""Phase 3: Exterior details, sky dome, player spawn, and final polish."""
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
# Sky — gradient sky for a dusk/twight feel
# ============================================================
print("=== Sky & Environment Update ===")

setprop('WorldEnvironment', 'environment', {
    'class':'Environment',
    'background_mode':3,
    'sky':{'class':'Sky','sky_material':{'class':'GradientSky','top_color':{'r':0.02,'g':0.02,'b':0.08,'a':1},'bottom_color':{'r':0.12,'g':0.08,'b':0.15,'a':1},'curve':0.8}},
    'ambient_light_source':2,
    'ambient_light_color':{'r':0.12,'g':0.12,'b':0.18,'a':1},
    'ambient_light_energy':0.4,
    'fog_enabled':True,
    'fog_light_color':{'r':0.05,'g':0.05,'b':0.12,'a':1},
    'fog_light_energy':0.5,
    'fog_density':0.06,
    'fog_aerial_perspective':0.5,
    'glow_enabled':True,
    'glow_intensity':1.0,
    'glow_strength':1.0,
    'glow_blend_mode':1,
    'glow_bloom':0.15,
    'volumetric_fog_enabled':True,
    'volumetric_fog_density':0.015,
    'volumetric_fog_albedo':{'r':0.1,'g':0.1,'b':0.2,'a':1},
    'volumetric_fog_emission':{'r':0.05,'g':0.08,'b':0.15,'a':1},
    'volumetric_fog_emission_energy':0.3,
    'tonemap_mode':2,
    'tonemap_white':1.2
})

print("  Sky updated!")

# ============================================================
# Exterior details — decorative railing, torches
# ============================================================
print("\n=== Exterior Details ===")

railing_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.18,'b':0.16,'a':1},
    'roughness':0.6,'metallic':0.2
}

# Plaza railing posts (left and right sides of plaza stairs)
for side, x in [('L', -9.0), ('R', 9.0)]:
    for i in range(6):
        z = 31.0 + i * 1.2
        name = f'Railing_{side}{i}'
        add('Architecture/Exterior/ExteriorPlaza', 'CSGBox3D', name)
        setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'size', {'x':0.3,'y':1.5,'z':0.3})
        setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':x,'y':0.75,'z':z})
        setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', railing_mat)

# Torch lights at entrance
for side, x in [('Left', -7.0), ('Right', 7.0)]:
    name = f'Torch_{side}'
    add('Architecture/Exterior/ExteriorPlaza', 'CSGCylinder3D', name)
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'radius', 0.15)
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'height', 2.0)
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'position', {'x':x,'y':3.0,'z':21.5})
    setprop(f'Architecture/Exterior/ExteriorPlaza/{name}', 'material_override', railing_mat)
    
    # Torch flame light
    light_name = f'TorchLight_{side}'
    add('.', 'OmniLight3D', light_name)
    setprop(light_name, 'position', {'x':x,'y':4.2,'z':21.5})
    setprop(light_name, 'light_color', {'r':1.0,'g':0.6,'b':0.2,'a':1})
    setprop(light_name, 'light_energy', 2.0)
    setprop(light_name, 'omni_range', 12.0)
    setprop(light_name, 'omni_attenuation', 1.2)

print("  Exterior details complete!")

# ============================================================
# Player spawn point
# ============================================================
print("\n=== Player Spawn ===")

add('.', 'Marker3D', 'PlayerSpawn')
setprop('PlayerSpawn', 'position', {'x':0,'y':1.0,'z':34.0})

print("  Player spawn set!")

# ============================================================
# Nave entrance arch (interior side, decorative)
# ============================================================
print("\n=== Nave Entrance Arch ===")

arch_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.33,'b':0.30,'a':1},
    'roughness':0.7,'metallic':0.1
}

# Arch sides
for side, x in [('Left', -4.0), ('Right', 4.0)]:
    name = f'NaveArchSide_{side}'
    add('Architecture/NaveCombiner', 'CSGBox3D', name)
    setprop(f'Architecture/NaveCombiner/{name}', 'size', {'x':1.0,'y':16.0,'z':1.0})
    setprop(f'Architecture/NaveCombiner/{name}', 'position', {'x':x,'y':8.0,'z':20.0})
    setprop(f'Architecture/NaveCombiner/{name}', 'material_override', arch_mat)

# Arch top
add('Architecture/NaveCombiner', 'CSGBox3D', 'NaveArchTop')
setprop('Architecture/NaveCombiner/NaveArchTop', 'size', {'x':10.0,'y':2.0,'z':1.0})
setprop('Architecture/NaveCombiner/NaveArchTop', 'position', {'x':0,'y':17.0,'z':20.0})
setprop('Architecture/NaveCombiner/NaveArchTop', 'material_override', arch_mat)

print("  Entrance arch complete!")

# ============================================================
# Spiritual Stones on pedestal (3 emissive gems)
# ============================================================
print("\n=== Spiritual Stones ===")

stone_colors = [
    ('RubyStone', {'r':0.8,'g':0.1,'b':0.1,'a':1}, {'r':1.0,'g':0.2,'b':0.1,'a':1}),
    ('EmeraldStone', {'r':0.1,'g':0.6,'b':0.15,'a':1}, {'r':0.1,'g':0.8,'b':0.2,'a':1}),
    ('SapphireStone', {'r':0.1,'g':0.2,'b':0.8,'a':1}, {'r':0.1,'g':0.3,'b':1.0,'a':1}),
]

for name, albedo, emission in stone_colors:
    x = -0.8 if 'Ruby' in name else (0 if 'Emerald' in name else 0.8)
    add('Architecture/NaveCombiner/AltarPlatform/PedestalTable', 'CSGSphere3D', name)
    path = f'Architecture/NaveCombiner/AltarPlatform/PedestalTable/{name}'
    setprop(path, 'radius', 0.12)
    setprop(path, 'position', {'x':x,'y':0.5,'z':0})
    setprop(path, 'material_override', {
        'class':'StandardMaterial3D',
        'albedo_color':albedo,
        'emission_enabled':True,
        'emission':emission,
        'emission_energy_multiplier':2.0,
        'roughness':0.15,
        'metallic':0.0,
        'transparency':0
    })

print("  Spiritual stones placed!")

# ============================================================
# Final save & verify
# ============================================================
print("\n=== Final Save ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")

r = call_tool('get_scene_tree', {})
if r.get('ok'):
    tree = r['tree']
    print(f"\nRoot: {tree['name']} ({tree['type']})")
    print(f"Top-level children: {len(tree.get('children', []))}")
    for child in tree.get('children', []):
        n = len(child.get('children', []))
        print(f"  - {child['name']} ({child['type']}) [{n} children]")
        if n > 0:
            for sub in child.get('children', []):
                sn = len(sub.get('children', []))
                print(f"      - {sub['name']} ({sub['type']}) [{sn} children]")

print("\nPhase 3 complete!")
