#!/usr/bin/env python3
"""Launch the scene in play mode via Godot RPC, wait, take screenshot, stop."""
import sys, os, time
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

# Play the scene
print("Launching scene in play mode...")
r = call_tool('play_scene', {'path': 'res://main_nave.tscn'})
print(f"Play: {r}")

# Wait for it to render
time.sleep(3)

# Take screenshot
print("Taking screenshot...")
r = call_tool('screenshot', {})
print(f"Screenshot: {r}")

# Stop the scene
time.sleep(1)
print("Stopping scene...")
r = call_tool('stop_scene', {})
print(f"Stop: {r}")

if r.get('ok') or 'error' in r:
    print("Done!")
