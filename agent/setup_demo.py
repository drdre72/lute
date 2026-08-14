#!/usr/bin/env python3
"""Attach demo_camera.gd to DemoCamera and set it as current, then save."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

# Set the script on DemoCamera
r = call_tool('node_set_property', {
    'node_path': 'DemoCamera',
    'property': 'script',
    'value': 'res://scripts/demo_camera.gd'
})
print(f"Script attached to DemoCamera: {r}")

# Make DemoCamera current
r = call_tool('node_set_property', {
    'node_path': 'DemoCamera',
    'property': 'current',
    'value': True
})
print(f"DemoCamera set as current: {r}")

# Set a good starting position for the hero shot
r = call_tool('node_set_property', {
    'node_path': 'DemoCamera',
    'property': 'position',
    'value': {'x': 0, 'y': 8, 'z': -5}
})
print(f"Position set: {r}")

r = call_tool('node_set_property', {
    'node_path': 'DemoCamera',
    'property': 'rotation_degrees',
    'value': {'x': -5, 'y': 0, 'z': 0}
})
print(f"Rotation set: {r}")

r = call_tool('node_set_property', {
    'node_path': 'DemoCamera',
    'property': 'fov',
    'value': 75
})
print(f"FOV set: {r}")

# Save
r = call_tool('scene_save', {})
print(f"Scene saved: {r}")
print("Done! Press F6 in Godot to run the scene in demo mode.")
