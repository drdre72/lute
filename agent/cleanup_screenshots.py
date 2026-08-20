#!/usr/bin/env python3
"""Delete old Godot editor screenshots, keeping only the most recent 2 sessions.
A 'session' is a group of screenshots taken within the same minute window."""
import os, glob
from datetime import datetime

screenshot_dir = os.path.expanduser(
    "~/Library/Application Support/Godot/app_userdata/Lute"
)

# Find all editor screenshots
pattern = os.path.join(screenshot_dir, "editor_screenshot_*.png")
files = glob.glob(pattern)

if not files:
    print("No screenshots found.")
    exit(0)

# Parse timestamps from filenames: editor_screenshot_2026-08-12T165230.png
def parse_ts(filepath):
    fname = os.path.basename(filepath)
    # Extract timestamp between "screenshot_" and ".png"
    ts_str = fname.replace("editor_screenshot_", "").replace(".png", "")
    try:
        return datetime.strptime(ts_str, "%Y-%m-%dT%H%M%S")
    except ValueError:
        return None

# Sort by timestamp descending (newest first)
tagged = [(parse_ts(f), f) for f in files if parse_ts(f) is not None]
tagged.sort(key=lambda x: x[0], reverse=True)

# Group by "session" — screenshots within 30 seconds of each other
sessions = []
current_session = []
last_ts = None

for ts, filepath in tagged:
    if last_ts and (last_ts - ts).total_seconds() > 30:
        sessions.append(current_session)
        current_session = []
    current_session.append(filepath)
    last_ts = ts

if current_session:
    sessions.append(current_session)

print(f"Found {len(files)} screenshots in {len(sessions)} sessions.")

# Keep newest 2 sessions, delete the rest
keep = set()
for session in sessions[:2]:
    keep.update(session)

deleted = 0
for ts, filepath in tagged:
    if filepath not in keep:
        size = os.path.getsize(filepath)
        os.remove(filepath)
        deleted += 1
        print(f"  Deleted: {os.path.basename(filepath)} ({size//1024}KB)")

print(f"\nKept {len(keep)} screenshots from {min(2, len(sessions))} most recent sessions.")
print(f"Deleted {deleted} old screenshots.")
