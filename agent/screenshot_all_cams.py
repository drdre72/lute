#!/usr/bin/env python3
"""Cycle through all cameras, set each as current, take screenshot from each."""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

def setprop(path, prop, value):
    return call_tool('node_set_property', {'node_path': path, 'property': prop, 'value': value})

cameras = [
    ('Cam_GrandOverview', 'grand_overview'),
    ('DemoCamera', 'temple_nave'),
    ('TownArea/Cam_TownStreet', 'town_street'),
    ('TownArea/TownCamera', 'town_aerial'),
    ('TownArea/LakeRegion/Cam_ForestLake', 'forest_lake'),
    ('TownArea/Cam_PathJourney', 'path_journey'),
]

for cam_path, label in cameras:
    print(f"\n=== {label} ({cam_path}) ===")
    
    # Set this camera as current
    r = setprop(cam_path, 'current', True)
    print(f"  Set current: {r}")
    
    # Wait for viewport to update
    time.sleep(1.5)
    
    # Take screenshot
    r = call_tool('screenshot', {})
    print(f"  Screenshot: {r}")
    
    # Unset current so next camera can take over
    setprop(cam_path, 'current', False)

print("\nDone — all 6 camera angles captured!")
