#!/usr/bin/env python3
"""Phase 2: Decorative details — stained glass side windows, column capitals,
floor inlay pattern, door frame detailing, and refined material pass."""
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
# Stained glass windows on side walls (high up, between pillars)
# ============================================================
print("=== Stained Glass Side Windows ===")

glass_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.05,'g':0.08,'b':0.15,'a':0.8},
    'emission_enabled':True,
    'emission':{'r':0.1,'g':0.2,'b':0.4,'a':1},
    'emission_energy_multiplier':1.5,
    'roughness':0.2,'metallic':0.0,
    'transparency':2
}

# Left side windows (between pillars, high on the wall)
for i, z in enumerate([12, 6, 0, -6, -12]):
    name = f'WindowL{i}'
    add('Architecture/NaveCombiner', 'CSGBox3D', name)
    setprop(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.1,'y':6.0,'z':3.0})
    setprop(f'Architecture/NaveCombiner/{name}', 'position', {'x':-11.0,'y':10.0,'z':z})
    setprop(f'Architecture/NaveCombiner/{name}', 'material_override', glass_mat)

# Right side windows
for i, z in enumerate([12, 6, 0, -6, -12]):
    name = f'WindowR{i}'
    add('Architecture/NaveCombiner', 'CSGBox3D', name)
    setprop(f'Architecture/NaveCombiner/{name}', 'size', {'x':0.1,'y':6.0,'z':3.0})
    setprop(f'Architecture/NaveCombiner/{name}', 'position', {'x':11.0,'y':10.0,'z':z})
    setprop(f'Architecture/NaveCombiner/{name}', 'material_override', glass_mat)

# Window accent lights (subtle blue glow from each side)
for i, z in enumerate([12, 0, -12]):
    for side, x in [('L', -10.5), ('R', 10.5)]:
        name = f'WinLight_{side}{i}'
        add('.', 'OmniLight3D', name)
        setprop(name, 'position', {'x':x,'y':10.0,'z':z})
        setprop(name, 'light_color', {'r':0.1,'g':0.2,'b':0.5,'a':1})
        setprop(name, 'light_energy', 0.5)
        setprop(name, 'omni_range', 8.0)

print("  Stained glass complete!")

# ============================================================
# Column capitals (decorative tops on pillars)
# ============================================================
print("\n=== Column Capitals ===")

capital_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.40,'g':0.38,'b':0.35,'a':1},
    'roughness':0.6,'metallic':0.15,
    'uv1_scale':{'x':1,'y':1,'z':1}
}

for side in ['L', 'R']:
    for i in range(6):
        name = f'Capital_{side}{i}'
        pillar_path = f'Architecture/NaveCombiner/Pillar_{side}{i}'
        add(pillar_path, 'CSGBox3D', name)
        setprop(f'{pillar_path}/{name}', 'size', {'x':2.4,'y':0.6,'z':2.4})
        setprop(f'{pillar_path}/{name}', 'position', {'x':0,'y':7.3,'z':0})
        setprop(f'{pillar_path}/{name}', 'material_override', capital_mat)

print("  Capitals complete!")

# ============================================================
# Door of Time frame (decorative arch around the slab)
# ============================================================
print("\n=== Door of Time Frame ===")

frame_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.20,'g':0.18,'b':0.16,'a':1},
    'roughness':0.5,'metallic':0.3,
    'emission_enabled':True,
    'emission':{'r':0.15,'g':0.1,'b':0.05,'a':1},
    'emission_energy_multiplier':0.5
}

# Left frame
add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorFrameLeft')
setprop('Architecture/NaveCombiner/DoorFrameLeft', 'size', {'x':0.8,'y':11.0,'z':0.8})
setprop('Architecture/NaveCombiner/DoorFrameLeft', 'position', {'x':-4.0,'y':6.5,'z':22.3})
setprop('Architecture/NaveCombiner/DoorFrameLeft', 'material_override', frame_mat)

# Right frame
add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorFrameRight')
setprop('Architecture/NaveCombiner/DoorFrameRight', 'size', {'x':0.8,'y':11.0,'z':0.8})
setprop('Architecture/NaveCombiner/DoorFrameRight', 'position', {'x':4.0,'y':6.5,'z':22.3})
setprop('Architecture/NaveCombiner/DoorFrameRight', 'material_override', frame_mat)

# Top frame
add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorFrameTop')
setprop('Architecture/NaveCombiner/DoorFrameTop', 'size', {'x':8.8,'y':1.0,'z':0.8})
setprop('Architecture/NaveCombiner/DoorFrameTop', 'position', {'x':0,'y':12.0,'z':22.3})
setprop('Architecture/NaveCombiner/DoorFrameTop', 'material_override', frame_mat)

# Glowing runes on the Door of Time (emissive strips)
add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorRuneLeft')
setprop('Architecture/NaveCombiner/DoorRuneLeft', 'size', {'x':0.1,'y':8.0,'z':0.7})
setprop('Architecture/NaveCombiner/DoorRuneLeft', 'position', {'x':-3.4,'y':6.0,'z':22.65})
setprop('Architecture/NaveCombiner/DoorRuneLeft', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.5,'g':0.3,'b':0.1,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.8,'g':0.5,'b':0.2,'a':1},
    'emission_energy_multiplier':3.0,
    'roughness':0.3
})

add('Architecture/NaveCombiner', 'CSGBox3D', 'DoorRuneRight')
setprop('Architecture/NaveCombiner/DoorRuneRight', 'size', {'x':0.1,'y':8.0,'z':0.7})
setprop('Architecture/NaveCombiner/DoorRuneRight', 'position', {'x':3.4,'y':6.0,'z':22.65})
setprop('Architecture/NaveCombiner/DoorRuneRight', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.5,'g':0.3,'b':0.1,'a':1},
    'emission_enabled':True,
    'emission':{'r':0.8,'g':0.5,'b':0.2,'a':1},
    'emission_energy_multiplier':3.0,
    'roughness':0.3
})

print("  Door frame complete!")

# ============================================================
# Floor inlay (decorative central path)
# ============================================================
print("\n=== Floor Inlay ===")

inlay_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.15,'g':0.12,'b':0.10,'a':1},
    'roughness':0.4,'metallic':0.3,
    'uv1_scale':{'x':1,'y':8,'z':1}
}

add('Architecture/NaveCombiner', 'CSGBox3D', 'FloorInlay')
setprop('Architecture/NaveCombiner/FloorInlay', 'size', {'x':3.0,'y':0.06,'z':40.0})
setprop('Architecture/NaveCombiner/FloorInlay', 'position', {'x':0,'y':0.03,'z':0})
setprop('Architecture/NaveCombiner/FloorInlay', 'material_override', inlay_mat)

# Circular inlay at altar area
add('Architecture/NaveCombiner', 'CSGCylinder3D', 'AltarInlay')
setprop('Architecture/NaveCombiner/AltarInlay', 'radius', 5.0)
setprop('Architecture/NaveCombiner/AltarInlay', 'sides', 24)
setprop('Architecture/NaveCombiner/AltarInlay', 'height', 0.06)
setprop('Architecture/NaveCombiner/AltarInlay', 'position', {'x':0,'y':0.03,'z':20.0})
setprop('Architecture/NaveCombiner/AltarInlay', 'material_override', inlay_mat)

print("  Floor inlay complete!")

# ============================================================
# Refined vault material (warmer, more detail)
# ============================================================
print("\n=== Refined Vault Material ===")

setprop('Architecture/NaveCombiner/NaveVault', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.42,'g':0.38,'b':0.32,'a':1},
    'roughness':0.82,'metallic':0.04,
    'uv1_scale':{'x':4,'y':12,'z':4}
})

# Save
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Done!")
