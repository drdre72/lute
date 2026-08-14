#!/usr/bin/env python3
"""Phase 15: More town buildings (back row complete), stone path edges,
garden plots, dock at lake, rowboat, more atmospheric detail."""
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

wood_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.35,'g':0.22,'b':0.12,'a':1},
    'roughness':0.8
}

roof_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.10,'a':1},
    'roughness':0.85
}

# ============================================================
# 1. Two more buildings filling the back row
# ============================================================
print("=== More Buildings ===")

new_buildings = [
    ('Tavern', 28.0, 128.0, 9.0, 8.0, 6.0),
    ('Blacksmith', 38.0, 128.0, 8.0, 8.0, 5.5),
]

for name, x, z, w, d, h in new_buildings:
    sa('TownArea/TownSquare', 'CSGBox3D', f'{name}_Walls')
    ss(f'TownArea/TownSquare/{name}_Walls', 'size', {'x':w,'y':h,'z':d})
    ss(f'TownArea/TownSquare/{name}_Walls', 'position', {'x':x,'y':h/2,'z':z})
    ss(f'TownArea/TownSquare/{name}_Walls', 'use_collision', True)
    ss(f'TownArea/TownSquare/{name}_Walls', 'material_override', wood_mat)
    
    # Door
    sa(f'TownArea/TownSquare/{name}_Walls', 'CSGBox3D', f'{name}_DoorCut')
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'operation', 2)
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'size', {'x':2.0,'y':3.5,'z':1.0})
    ss(f'TownArea/TownSquare/{name}_Walls/{name}_DoorCut', 'position', {'x':0,'y':1.75,'z':d/2})
    
    # Windows
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

# Blacksmith forge glow
sa('TownArea/TownSquare', 'OmniLight3D', 'ForgeGlow')
ss('TownArea/TownSquare/ForgeGlow', 'position', {'x':38.0,'y':2.0,'z':127.0})
ss('TownArea/TownSquare/ForgeGlow', 'light_color', {'r':1.0,'g':0.3,'b':0.1,'a':1})
ss('TownArea/TownSquare/ForgeGlow', 'light_energy', 3.0)
ss('TownArea/TownSquare/ForgeGlow', 'omni_range', 10.0)

# Anvil
sa('TownArea/TownSquare', 'CSGBox3D', 'Anvil')
ss('TownArea/TownSquare/Anvil', 'size', {'x':0.8,'y':1.0,'z':0.5})
ss('TownArea/TownSquare/Anvil', 'position', {'x':36.0,'y':0.5,'z':127.0})
ss('TownArea/TownSquare/Anvil', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.10,'b':0.10,'a':1},
    'roughness':0.3,'metallic':0.8
})

print("  Tavern + Blacksmith built!")

# ============================================================
# 2. Garden plots near houses
# ============================================================
print("\n=== Garden Plots ===")

garden_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.10,'g':0.15,'b':0.06,'a':1},
    'roughness':0.95
}

for i, (x, z) in enumerate([(10, 115), (50, 115)]):
    name = f'GardenPlot{i}'
    sa('TownArea/TownSquare', 'CSGBox3D', name)
    ss(f'TownArea/TownSquare/{name}', 'size', {'x':3.0,'y':0.2,'z':3.0})
    ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.1,'z':z})
    ss(f'TownArea/TownSquare/{name}', 'material_override', garden_mat)
    
    # Garden plants
    for pi in range(6):
        px = x + random.uniform(-1.2, 1.2)
        pz = z + random.uniform(-1.2, 1.2)
        pname = f'{name}_Plant{pi}'
        sa('TownArea/TownSquare', 'CSGSphere3D', pname)
        ss(f'TownArea/TownSquare/{pname}', 'radius', 0.25)
        ss(f'TownArea/TownSquare/{pname}', 'position', {'x':px,'y':0.3,'z':pz})
        ss(f'TownArea/TownSquare/{pname}', 'material_override', {
            'class':'StandardMaterial3D',
            'albedo_color':{'r':0.08,'g':0.25,'b':0.06,'a':1},
            'roughness':0.95
        })

print("  2 garden plots with plants!")

# ============================================================
# 3. Dock at lake
# ============================================================
print("\n=== Lake Dock ===")

dock_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.30,'g':0.20,'b':0.10,'a':1},
    'roughness':0.8
}

# Dock planks
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'Dock')
ss('TownArea/LakeRegion/LakeGeometry/Dock', 'size', {'x':3.0,'y':0.3,'z':8.0})
ss('TownArea/LakeRegion/LakeGeometry/Dock', 'position', {'x':20.0,'y':0.3,'z':172.0})
ss('TownArea/LakeRegion/LakeGeometry/Dock', 'use_collision', True)
ss('TownArea/LakeRegion/LakeGeometry/Dock', 'material_override', dock_mat)

# Dock posts
for i, z in enumerate(range(168, 177, 2)):
    name = f'DockPost{i}'
    sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', name)
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'size', {'x':0.2,'y':1.5,'z':0.2})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'position', {'x':18.8,'y':0.5,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}', 'material_override', dock_mat)
    
    sa('TownArea/LakeRegion/LakeGeometry', f'{name}R', 'CSGBox3D')
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}R', 'size', {'x':0.2,'y':1.5,'z':0.2})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}R', 'position', {'x':21.2,'y':0.5,'z':z})
    ss(f'TownArea/LakeRegion/LakeGeometry/{name}R', 'material_override', dock_mat)

# Rowboat
sa('TownArea/LakeRegion/LakeGeometry', 'CSGBox3D', 'Rowboat')
ss('TownArea/LakeRegion/LakeGeometry/Rowboat', 'size', {'x':1.5,'y':0.5,'z':4.0})
ss('TownArea/LakeRegion/LakeGeometry/Rowboat', 'position', {'x':20.0,'y':0.4,'z':168.0})
ss('TownArea/LakeRegion/LakeGeometry/Rowboat', 'material_override', {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.25,'g':0.15,'b':0.07,'a':1},
    'roughness':0.8
})

# Boat interior cutout
sa('TownArea/LakeRegion/LakeGeometry/Rowboat', 'CSGBox3D', 'BoatHollow')
ss('TownArea/LakeRegion/LakeGeometry/Rowboat/BoatHollow', 'operation', 2)
ss('TownArea/LakeRegion/LakeGeometry/Rowboat/BoatHollow', 'size', {'x':1.2,'y':0.4,'z':3.5})
ss('TownArea/LakeRegion/LakeGeometry/Rowboat/BoatHollow', 'position', {'x':0,'y':0.2,'z':0})

print("  Lake dock + rowboat built!")

# ============================================================
# 4. Stone path edges in town
# ============================================================
print("\n=== Path Edges ===")

edge_mat = {
    'class':'StandardMaterial3D',
    'albedo_color':{'r':0.22,'g':0.20,'b':0.17,'a':1},
    'roughness':0.85
}

# Path from gate to fountain
for z in range(98, 128, 2):
    for side, x in [('L', 27.0), ('R', 33.0)]:
        name = f'PathEdge_{side}_{z}'
        sa('TownArea/TownSquare', 'CSGBox3D', name)
        ss(f'TownArea/TownSquare/{name}', 'size', {'x':0.3,'y':0.15,'z':1.5})
        ss(f'TownArea/TownSquare/{name}', 'position', {'x':x,'y':0.07,'z':z})
        ss(f'TownArea/TownSquare/{name}', 'material_override', edge_mat)

print("  Stone path edges placed!")

# ============================================================
# 5. Save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")
print("Phase 15 complete!")
