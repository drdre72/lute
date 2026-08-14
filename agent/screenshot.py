#!/usr/bin/env python3
"""On-demand screenshot from Godot editor viewport via RPC.
Usage: .venv/bin/python3 screenshot.py [output_path]
If no path given, saves to Godot user data dir with timestamp."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from tools import call_tool

path = sys.argv[1] if len(sys.argv) > 1 else ""
r = call_tool('screenshot', {'path': path})
print(r)
if r.get('ok'):
    print(f"Screenshot: {r['path']}")
