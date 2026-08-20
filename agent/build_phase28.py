#!/usr/bin/env python3
"""Phase 28: Integrate Open World Database for chunk-based streaming.
Adds OpenWorldDatabase node, OWDBPosition trackers at key locations,
and configures chunk sizes for our world layout."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def add(parent, type, name):
    r = call_tool('node_add', {'parent_path': parent, 'type': type, 'name': name})
    return r

def setprop(path, prop, value):
    r = call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})
    return r

def sa(parent, type, name):
    r = add(parent, type, name)
    print(f"  +{name}: {r}")
    return r

def ss(path, prop, value):
    r = setprop(path, prop, value)
    print(f"  .{path.split('/')[-1]}.{prop}: {r}")
    return r

# ============================================================
# 1. Add OpenWorldDatabase node at scene root
# ============================================================
print("=== Adding OpenWorldDatabase ===")

sa('.', 'OpenWorldDatabase', 'OWDB')

# Configure chunk sizes for our world:
# - 8.0 units: small props (trees, rocks, bushes) — fine-grained streaming
# - 16.0 units: medium props (buildings, structures)  
# - 64.0 units: large terrain patches — coarse streaming
ss('OWDB', 'chunk_sizes', [8.0, 16.0, 64.0])
ss('OWDB', 'chunk_load_range', 4)  # Load 4 chunks around position in each direction
ss('OWDB', 'threshold_ratio', 0.25)
ss('OWDB', 'batch_processing_enabled', True)
ss('OWDB', 'batch_time_limit_ms', 10.0)
ss('OWDB', 'batch_interval_ms', 50.0)
ss('OWDB', 'follow_editor_camera', True)
ss('OWDB', 'load_all_chunks', True)  # Load all in editor for visibility
ss('OWDB', 'debug_enabled', False)

print("  OWDB configured!")

# ============================================================
# 2. Add OWDBPosition trackers at key world locations
# ============================================================
print("\n=== Adding OWDBPosition Trackers ===")

# Player spawn position (town center)
sa('OWDB', 'OWDBPosition', 'Pos_TownCenter')
ss('OWDB/Pos_TownCenter', 'position', {'x': 30, 'y': 0, 'z': 115})
print("  Town center tracker at (30, 0, 115)")

# Temple position
sa('OWDB', 'OWDBPosition', 'Pos_Temple')
ss('OWDB/Pos_Temple', 'position', {'x': 0, 'y': 0, 'z': 0})
print("  Temple tracker at (0, 0, 0)")

# Forest position
sa('OWDB', 'OWDBPosition', 'Pos_Forest')
ss('OWDB/Pos_Forest', 'position', {'x': 0, 'y': 0, 'z': 160})
print("  Forest tracker at (0, 0, 160)")

# Lake/Castle position
sa('OWDB', 'OWDBPosition', 'Pos_Lake')
ss('OWDB/Pos_Lake', 'position', {'x': 20, 'y': 0, 'z': 185})
print("  Lake tracker at (20, 0, 185)")

# Mountain position
sa('OWDB', 'OWDBPosition', 'Pos_Mountain')
ss('OWDB/Pos_Mountain', 'position', {'x': 0, 'y': 0, 'z': 230})
print("  Mountain tracker at (0, 0, 230)")

# ============================================================
# 3. Save scene — OWDB auto-saves its database on scene save
# ============================================================
print("\n=== Saving ===")
r = call_tool('scene_save', {})
print(f"Save: {r}")

# ============================================================
# 4. Verify OWDB is working
# ============================================================
print("\n=== Verifying OWDB ===")
r = call_tool('node_get_properties', {'node_path': 'OWDB'})
props = r.get('properties', {})
print(f"  Loaded nodes: {props.get('get_currently_loaded_nodes', 'N/A')}")
print(f"  Total DB nodes: {props.get('get_total_database_nodes', 'N/A')}")
print(f"  Active positions: {props.get('get_active_position_count', 'N/A')}")

print("\nPhase 28 complete — Open World Database integrated!")
print("  - OpenWorldDatabase node with 8/16/64 chunk sizes")
print("  - 5 OWDBPosition trackers (town, temple, forest, lake, mountain)")
print("  - Editor camera following enabled for live chunk streaming")
print("  - load_all_chunks=True for editor visibility")
