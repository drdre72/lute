#!/usr/bin/env python3
"""Spatial probing via the S&Box editor's built-in MCP server.

The agent model has no vision layer — it cannot process screenshots. This
script replaces "looking at the screen" with structured text from live
scene queries: object positions, world-space bounds, and downward raycasts
that find ground contact.

Usage:
    python sbox_eyes.py                      # full report of all objects
    python sbox_eyes.py Acropolis            # focus on a named object
    python sbox_eyes.py Acropolis --rays 9   # more ground-probe rays
    python sbox_eyes.py --list-tools          # show available MCP tools
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.request

MCP_URL = "http://127.0.0.1:7269/mcp"
M_TO_UNITS = 39.3701  # S&Box units per meter (inches)


def mcp_call(method: str, params: dict | None = None) -> dict:
    """Send a JSON-RPC POST to the S&Box editor MCP server."""
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
    }
    if params is not None:
        payload["params"] = params

    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        MCP_URL,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        result = json.loads(resp.read().decode("utf-8"))

    if "error" in result:
        raise RuntimeError(f"MCP error: {result['error']}")
    return result.get("result", {})


def call_tool(name: str, arguments: dict | None = None) -> dict:
    """Call an MCP tool by name."""
    params = {"name": name}
    if arguments:
        params["arguments"] = arguments
    result = mcp_call("tools/call", params)
    # MCP tool results come back as {content: [{type: "text", text: "..."}]}
    if isinstance(result, dict) and "content" in result:
        for item in result["content"]:
            if item.get("type") == "text":
                try:
                    return json.loads(item["text"])
                except (json.JSONDecodeError, KeyError):
                    return {"_raw": item["text"]}
    return result


def list_tools() -> list:
    """List all available MCP tools."""
    result = mcp_call("tools/list")
    return result.get("tools", [])


def find_objects(name: str = "") -> list:
    """Find game objects by name substring."""
    args = {"limit": 100}
    if name:
        args["name"] = name
    result = call_tool("find_game_objects", args)
    return result.get("Results", [])


def get_object(obj_id: str, include_props: bool = False) -> dict:
    """Get full details of a game object by GUID."""
    return call_tool("get_game_object", {
        "id": obj_id,
        "includeComponentProperties": include_props,
    })


def scene_trace(from_pos: str, to_pos: str) -> dict:
    """Cast a ray from one point to another and return what it hits."""
    return call_tool("scene_trace", {"from": from_pos, "to": to_pos})


def get_editor_camera() -> dict:
    """Get the editor camera's position and angles."""
    return call_tool("get_editor_camera")


def set_editor_camera(position: str, angles: str = "") -> dict:
    """Move the editor camera."""
    args = {"position": position}
    if angles:
        args["angles"] = angles
    return call_tool("set_editor_camera", args)


def fmt_vec(v) -> str:
    """Format a Vector3-like value."""
    if isinstance(v, dict):
        return f"({v.get('x', 0):.1f}, {v.get('y', 0):.1f}, {v.get('z', 0):.1f})"
    if isinstance(v, str):
        return v
    if isinstance(v, list) and len(v) == 3:
        return f"({v[0]:.1f}, {v[1]:.1f}, {v[2]:.1f})"
    return str(v)


def vec_to_str(v) -> str:
    """Convert a Vector3-like value to 'x,y,z' string for MCP calls."""
    if isinstance(v, dict):
        return f"{v.get('x', 0)},{v.get('y', 0)},{v.get('z', 0)}"
    if isinstance(v, list) and len(v) == 3:
        return f"{v[0]},{v[1]},{v[2]}"
    if isinstance(v, str):
        return v
    return str(v)


def units_to_meters(val: float) -> float:
    return val / M_TO_UNITS


def probe_ground(pos: list, ray_length: float = 50000) -> dict:
    """Cast a ray downward from a position and return ground contact info.

    Ray starts at the given position and goes straight down by ray_length units.
    Returns hit status, hit position, distance, and what was hit.
    """
    from_str = f"{pos[0]},{pos[1]},{pos[2]}"
    to_str = f"{pos[0]},{pos[1]},{pos[2] - ray_length}"
    return scene_trace(from_str, to_str)


def full_report(focus_name: str = "", num_rays: int = 5) -> str:
    """Generate a full spatial report as text."""
    lines = []
    lines.append("=== S&Box Spatial Probe Report ===")
    lines.append("")

    # 1. List all objects
    lines.append("--- All Game Objects ---")
    all_objects = find_objects("")
    for obj in all_objects:
        lines.append(f"  {obj.get('Name', '?')} id={obj.get('Id', '?')} "
                     f"components: {', '.join(obj.get('Components', []))}")
    lines.append("")

    # 2. Focus object details
    focus_objects = []
    if focus_name:
        focus_objects = find_objects(focus_name)
    elif all_objects:
        focus_objects = all_objects

    for obj in focus_objects:
        obj_id = obj.get("Id")
        if not obj_id:
            continue

        lines.append(f"--- Object: {obj.get('Name', '?')} ---")
        details = get_object(str(obj_id), include_props=False)

        world_pos = details.get("WorldPosition", {})
        world_scale = details.get("WorldScale", {})
        world_rot = details.get("WorldRotation", {})

        # Parse position components (MCP returns strings like "x,y,z")
        if isinstance(world_pos, dict):
            cx = world_pos.get("x", 0)
            cy = world_pos.get("y", 0)
            cz = world_pos.get("z", 0)
        elif isinstance(world_pos, str):
            parts = [float(p) for p in world_pos.split(",")]
            cx, cy, cz = parts[0], parts[1], parts[2]
        else:
            cx, cy, cz = 0, 0, 0

        # Parse scale components
        if isinstance(world_scale, dict):
            sx = world_scale.get("x", 1)
            sy = world_scale.get("y", 1)
        elif isinstance(world_scale, str):
            parts = [float(p) for p in world_scale.split(",")]
            sx = parts[0] if len(parts) > 0 else 1
            sy = parts[1] if len(parts) > 1 else 1
        else:
            sx, sy = 1, 1

        lines.append(f"  World Position: {fmt_vec(world_pos)}")
        lines.append(f"  World Scale: {fmt_vec(world_scale)}")
        lines.append(f"  World Rotation: {fmt_vec(world_rot)}")

        pos_m = units_to_meters(cz)
        lines.append(f"  Position Z in meters: {pos_m:.2f} m")

        components = details.get("Components", [])
        comp_types = [c.get("Type", "?") for c in components]
        lines.append(f"  Components: {', '.join(comp_types)}")

        children = details.get("Children", [])
        if children:
            lines.append(f"  Children: {', '.join(c.get('Name', '?') for c in children)}")

        lines.append("")

        # 3. Ground probes — cast rays downward from a grid of points
        # around and above the object's world position
        # (cx, cy, cz, sx, sy already parsed above)

        # Probe from above the object, looking down
        # Use a grid of points centered on the object
        half_x = 500 * sx  # spread probes across the footprint
        half_y = 500 * sy
        ray_start_z = cz + 2000  # start well above the object
        ray_length = 10000  # cast far down

        lines.append("--- Ground Probes (downward raycasts) ---")
        lines.append(f"  Ray start Z: {ray_start_z:.0f} (above object), "
                     f"cast length: {ray_length:.0f} units down")

        # Generate probe points
        offsets = []
        if num_rays <= 1:
            offsets = [(0, 0)]
        elif num_rays <= 5:
            offsets = [(0, 0), (-half_x, 0), (half_x, 0),
                       (0, -half_y), (0, half_y)]
        elif num_rays <= 9:
            offsets = [
                (0, 0),
                (-half_x, 0), (half_x, 0), (0, -half_y), (0, half_y),
                (-half_x, -half_y), (half_x, -half_y),
                (-half_x, half_y), (half_x, half_y),
            ]
        else:
            # Grid
            import math
            n = int(math.sqrt(num_rays))
            for ix in range(n):
                for iy in range(n):
                    ox = -half_x + (2 * half_x * ix / max(1, n - 1))
                    oy = -half_y + (2 * half_y * iy / max(1, n - 1))
                    offsets.append((ox, oy))

        for i, (ox, oy) in enumerate(offsets):
            px = cx + ox
            py = cy + oy
            pz = ray_start_z

            label = "center" if (ox == 0 and oy == 0) else f"offset({ox:.0f},{oy:.0f})"
            trace = probe_ground([px, py, pz], ray_length)

            hit = trace.get("Hit", False)
            if hit:
                end_pos = trace.get("EndPosition", {})
                if isinstance(end_pos, dict):
                    hit_z = end_pos.get("z", 0)
                elif isinstance(end_pos, str):
                    hit_z = float(end_pos.split(",")[2])
                else:
                    hit_z = 0
                distance = trace.get("Distance", 0)
                hit_obj = trace.get("GameObject", {})
                hit_name = hit_obj.get("Name", "?") if isinstance(hit_obj, dict) else "?"
                hit_comp = trace.get("Component", "?")

                ground_m = units_to_meters(hit_z)
                delta_m = units_to_meters(cz - hit_z) if cz > hit_z else units_to_meters(hit_z - cz)

                lines.append(
                    f"  [{i}] {label}: HIT at Z={hit_z:.1f} "
                    f"({ground_m:.2f} m) dist={distance:.1f} "
                    f"obj='{hit_name}' comp={hit_comp} "
                    f"| object base Z={cz:.1f}, delta={delta_m:.2f} m"
                )
            else:
                lines.append(
                    f"  [{i}] {label}: MISS (no ground found within "
                    f"{ray_length:.0f} units below)"
                )

        lines.append("")

    # 4. Editor camera state
    lines.append("--- Editor Camera ---")
    try:
        cam = get_editor_camera()
        lines.append(f"  Position: {fmt_vec(cam.get('Position', {}))}")
        lines.append(f"  Angles: {fmt_vec(cam.get('Angles', {}))}")
        lines.append(f"  FOV: {cam.get('FieldOfView', 0):.1f}")
    except Exception as e:
        lines.append(f"  (could not read camera: {e})")
    lines.append("")

    lines.append("=== End of Report ===")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Spatial probing via S&Box MCP server — 'eyes' for agents without vision."
    )
    parser.add_argument("focus", nargs="?", default="",
                        help="Object name to focus on (substring match). Empty = all objects.")
    parser.add_argument("--rays", type=int, default=5,
                        help="Number of ground-probe rays to cast (1, 5, 9, or grid).")
    parser.add_argument("--list-tools", action="store_true",
                        help="List all available MCP tools and exit.")
    parser.add_argument("--trace", nargs=2, metavar=("FROM", "TO"),
                        help="Cast a ray from 'x,y,z' to 'x,y,z'.")
    parser.add_argument("--find", metavar="NAME",
                        help="Find game objects by name substring.")
    args = parser.parse_args()

    try:
        if args.list_tools:
            tools = list_tools()
            for t in tools:
                print(f"  {t.get('name', '?')}: {t.get('description', '')[:80]}")
            return 0

        if args.find:
            results = find_objects(args.find)
            for obj in results:
                print(f"  {obj.get('Name', '?')} id={obj.get('Id', '?')} "
                      f"components: {', '.join(obj.get('Components', []))}")
            return 0

        if args.trace:
            result = scene_trace(args.trace[0], args.trace[1])
            print(json.dumps(result, indent=2, default=str))
            return 0

        print(full_report(focus_name=args.focus, num_rays=args.rays))
        return 0

    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        print(f"(Is the S&Box editor running with MCP enabled on {MCP_URL}?)",
              file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
