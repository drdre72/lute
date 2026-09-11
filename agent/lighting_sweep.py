"""
lighting_sweep.py — Drive the directional light through fixed time-of-day
states, capture at each, and ask Qwen to verify the mood matches the
intended atmosphere (noon, dusk, night with torches).

The PRD specifies noon/dusk/night mood. This tool verifies those
atmospheric requirements — not just "is this geometrically correct" but
"does this hit the mood it's supposed to."

Usage:
    python agent/lighting_sweep.py                  # sweep all states, report
    python agent/lighting_sweep.py --only dusk       # only one state
    python agent/lighting_sweep.py --grid           # overlay grid on captures
"""
import sys, os, json, time, io, base64, argparse, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vision_lib import (VisionLib, vantage, save_img, ask_vision, pixel_check,
                        overlay_grid)
from sbox_eyes import call_tool, find_objects, get_object

# Market center
M = 39.37
CX, CY = 15748.0, 15748.0
FOUND_TOP = 2.5 * M  # ~99.4

# Time-of-day states: (name, light_rotation_quaternion, light_color,
#   ambient_color, prompt, keywords)
# The directional light's rotation controls sun angle.
# Quaternion format: "x,y,z,w"
# Noon: sun high overhead, bright white
# Dusk: sun low on horizon, warm orange, longer shadows
# Night: sun below horizon (dark), torches provide warm pools
TIME_STATES = [
    {
        "name": "noon",
        # Sun nearly overhead — rotation pointing straight down
        # Quaternion for ~80° elevation: mostly looking down
        "light_rotation": "-0.161728993,0.390448302,0.346828997,0.837319016",
        "light_color": "0.91373,0.98039,1,1",
        "ambient_color": "0.23721,0.23721,0.23721,1",
        "prompt": (
            "You are looking at a medieval marketplace at midday. "
            "Verify the lighting reads as bright noon — clear daylight, "
            "sharp shadows, no torch glow needed. "
            "Report if the scene is too dark, too orange, or doesn't read as daytime."
        ),
        "keywords": ["too dark", "too orange", "nighttime", "dim"],
    },
    {
        "name": "dusk",
        # Sun low on western horizon — rotate to ~15° elevation
        # Approximate quaternion for low sun angle
        "light_rotation": "-0.6,0.3,0.2,0.7",
        "light_color": "1,0.6,0.3,1",  # warm orange
        "ambient_color": "0.15,0.1,0.08,1",  # dim warm ambient
        "prompt": (
            "You are looking at a medieval marketplace at dusk. "
            "Verify the lighting reads as golden hour — warm orange light, "
            "long shadows, torches should be starting to glow. "
            "Report if the scene reads as noon (too bright/white) or night "
            "(too dark to see structure)."
        ),
        "keywords": ["too bright", "noon", "midday", "too dark to see"],
    },
    {
        "name": "night",
        # Sun below horizon — very low angle, minimal light
        "light_rotation": "-0.8,0.1,0.05,0.4",
        "light_color": "0.1,0.1,0.15,1",  # very dim blue
        "ambient_color": "0.05,0.05,0.08,1",  # minimal ambient
        "prompt": (
            "You are looking at a medieval marketplace at night. "
            "Verify the lighting reads as nighttime — dark overall, but "
            "torch lights should provide warm pools of orange light near "
            "the gates and towers. "
            "Report if the scene is fully black (torches not working) or "
            "too bright (doesn't read as night)."
        ),
        "keywords": ["fully black", "too bright", "daytime", "noon"],
    },
]


def find_directional_light():
    """Find the Directional Light GameObject in the live scene."""
    objs = find_objects("Directional Light")
    if not objs:
        print("  [WARN] Directional Light not found")
        return None
    # Get the full object with component details
    full = get_object(objs[0]["Id"], include_props=True)
    return full


def set_light_state(state):
    """Apply a time-of-day state to the directional light.
    Sets rotation (sun angle), light color, and ambient color."""
    light = find_directional_light()
    if not light:
        print("  [WARN] Cannot set light state — light not found")
        return False

    light_id = light.get("Id", "")
    if not light_id:
        return False

    # Set the GameObject rotation (sun angle)
    try:
        call_tool("set_game_object", {
            "id": light_id,
            "angles": state["light_rotation"],
        })
    except Exception as e:
        print(f"  [WARN] set_game_object failed: {e}")

    # Find the DirectionalLight component and set its color
    components = light.get("Components", [])
    for comp in components:
        comp_type = comp.get("__type", comp.get("Type", ""))
        comp_id = comp.get("__guid", comp.get("Id", ""))
        if "DirectionalLight" in comp_type:
            try:
                call_tool("set_component", {
                    "id": comp_id,
                    "properties": {"LightColor": state["light_color"]},
                })
            except Exception as e:
                print(f"  [WARN] set_component (LightColor) failed: {e}")
        elif "AmbientLight" in comp_type:
            try:
                call_tool("set_component", {
                    "id": comp_id,
                    "properties": {"Color": state["ambient_color"]},
                })
            except Exception as e:
                print(f"  [WARN] set_component (AmbientColor) failed: {e}")

    return True


def restore_default_light():
    """Restore the light to its original scene-defined state."""
    print("  Restoring default lighting...")
    set_light_state(TIME_STATES[0])  # noon = default


def sweep_one(vl, state, use_grid=False):
    """Set the light state, capture, and ask Qwen about the mood.
    Returns a report entry."""
    name = state["name"]
    print(f"\n--- LIGHTING: {name} ---")

    if not set_light_state(state):
        return {"type": "lighting", "state": name, "verdict": "not_visible",
                "notes": "light not found", "image": None}

    # Wait for lighting to update
    time.sleep(2)

    # Capture from the plaza interior viewpoint (elevated overview)
    pos, angles = vantage(CX, CY, FOUND_TOP + 200, 3000, 4000, 0)
    vl.teleport(pos, angles=angles, via="merlyn")
    img = vl.capture(via="merlyn", flash=(name == "night"),
                     flash_threshold=0.40, flash_radius=3000,
                     flash_pos=(CX, CY, FOUND_TOP))
    if not img:
        print("  CAPTURE FAILED")
        return {"type": "lighting", "state": name, "verdict": "not_visible",
                "notes": "capture failed", "image": None}

    path = save_img(img, f"light_{name}")
    ok, stats = pixel_check(img)
    if not ok:
        print(f"  Frame rejected: {stats['dominant_pct']:.0%} single color")
        return {"type": "lighting", "state": name, "verdict": "not_visible",
                "notes": "frame rejected", "image": path}
    print(f"  Pixel: bright={stats['bright_pct']:.0%} mid={stats['mid_pct']:.0%} dark={stats['dark_pct']:.0%}")

    vlm_img = overlay_grid(img) if use_grid else img
    answer = ask_vision(vlm_img, state["prompt"])
    print(f"  Qwen: {answer[:300]}")

    # Scan for discrepancy keywords (same logic as macro pass)
    answer_lower = answer.lower()
    negations = ["not ", "no ", "nothing", "none", "without", "isn't", "aren't", "wasn't", "weren't"]
    found = []
    for kw in state["keywords"]:
        idx = 0
        while True:
            pos = answer_lower.find(kw, idx)
            if pos == -1:
                break
            context_before = answer_lower[max(0, pos - 25):pos]
            is_negated = any(neg in context_before for neg in negations)
            if not is_negated:
                found.append(kw)
                break
            idx = pos + len(kw)

    verdict = "ok" if not found else "mood_mismatch"
    print(f"  >> {verdict}  issues: {found if found else 'none'}")

    return {
        "type": "lighting", "state": name, "verdict": verdict,
        "mood_issues": found, "vlm_response": answer,
        "notes": ", ".join(found) if found else f"reads as {name}",
        "image": path,
    }


def main():
    parser = argparse.ArgumentParser(description="Lute lighting/time-of-day sweep")
    parser.add_argument("--only", default="", help="Only sweep this state (noon/dusk/night)")
    parser.add_argument("--grid", action="store_true", help="Overlay pixel grid on captures")
    args = parser.parse_args()

    states = TIME_STATES
    if args.only:
        states = [s for s in TIME_STATES if args.only.lower() in s["name"]]

    print("=== Lute Lighting/Time-of-Day Sweep ===")
    print(f"States: {[s['name'] for s in states]}")

    vl = VisionLib()
    report = []

    for state in states:
        entry = sweep_one(vl, state, use_grid=args.grid)
        report.append(entry)

    # Restore default lighting
    restore_default_light()

    # Final report
    print(f"\n{'='*60}")
    print("  LIGHTING SWEEP REPORT")
    print(f"{'='*60}")
    for r in report:
        verdict = r.get("verdict", "?")
        status = {"ok": "OK", "mood_mismatch": "MOOD MISMATCH",
                  "not_visible": "?"}.get(verdict, "?")
        print(f"  {status:14s} {r['state']:10s} {verdict:14s} {r.get('notes', '')}")

    # Save report
    report_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                                "scrap", "lighting_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"\nReport saved: {report_path}")


if __name__ == "__main__":
    main()
