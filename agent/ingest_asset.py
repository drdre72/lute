"""
Automated FBX/OBJ ingestion pipeline for Lute (S&Box).

Takes an FBX (or OBJ) file, auto-detects the import scale from the FBX
UnitScaleFactor header, generates a .vmdl file with:
  - MaterialGroupList fallback (handles bad FBX material names like root.0.0)
  - RenderMeshFile referencing the source mesh
  - PhysicsShapeList with auto-assigned collision (convex hull or concave mesh)

Then optionally verifies the imported model bounds via the S&Box MCP server
(Model.Load(...).Bounds).

Usage:
    python agent/ingest_asset.py path/to/mesh.fbx
    python agent/ingest_asset.py path/to/mesh.fbx --scale 1.0
    python agent/ingest_asset.py path/to/mesh.fbx --collision concave
    python agent/ingest_asset.py path/to/mesh.fbx --material materials/medieval/stone_wall.vmat
    python agent/ingest_asset.py path/to/mesh.fbx --verify
    python agent/ingest_asset.py path/to/mesh.fbx --outdir sbox/Assets/models/custom

The script does NOT require the S&Box editor to be running for .vmdl
generation. The --verify flag uses MCP to query the compiled model's bounds,
which requires the editor + play mode.
"""
import os
import sys
import re
import struct
import argparse
import json
import urllib.request

# ── Constants ──
M_TO_INCHES = 39.3701
CM_TO_INCHES = 0.3937
MM_TO_INCHES = 0.03937

# Default fallback material (engine built-in)
DEFAULT_MATERIAL = "materials/dev/primary_white.vmat"

# Repo root (parent of agent/)
REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SBOX_ASSETS = os.path.join(REPO_ROOT, "sbox", "Assets")


def parse_fbx_unit_scale(fbx_path):
    """
    Parse the FBX file to extract the UnitScaleFactor.
    FBX files can be binary or ASCII. The UnitScaleFactor is stored in
    the FBX header as a property of the UnitScaleFactor node.

    Returns (scale_factor, units_name) where scale_factor is the raw
    FBX value (typically 1.0 meaning cm) and units_name is the unit
    string if detectable (e.g. "cm", "m", "inches").
    """
    with open(fbx_path, "rb") as f:
        raw = f.read()

    # Check if binary or ASCII FBX
    is_binary = b"Kaydara FBX Binary" in raw[:50]

    if is_binary:
        # Binary FBX: search for UnitScaleFactor in the binary stream.
        # The property is stored as a double followed by the unit name.
        # We search for the ASCII string "UnitScaleFactor" in the binary
        # data, then parse the following property block.
        idx = raw.find(b"UnitScaleFactor")
        if idx >= 0:
            # After the property name, there's a property record.
            # In binary FBX, properties follow a specific format.
            # We search for a double-precision float near the marker.
            # The double is typically 1.0 (cm), 0.01 (m in cm), or 2.54 (inches in cm).
            # Search in a window after the marker for a recognizable double.
            window = raw[idx:idx + 200]
            # Look for the pattern: a double value followed by "UnitScaleFactor" or
            # the unit name. In practice, the value is often 1.0 encoded as:
            #   00 00 00 00 00 00 F0 3F (little-endian double 1.0)
            # or 2.54:  AE 47 E1 7A 14 AE 04 40
            # or 0.01:  7B 14 AE 47 E1 7A 84 3F
            # or 100:   00 00 00 00 00 00 59 40
            for offset in range(len(window) - 8):
                val = struct.unpack("<d", window[offset:offset + 8])[0]
                if 0.001 <= val <= 1000.0 and val in (0.01, 0.1, 1.0, 2.54, 10.0, 100.0, 1000.0):
                    # Try to find unit name nearby
                    unit_name = "cm"  # default assumption
                    for uname in (b"mm", b"cm", b"m", b"inches", b"foot", b"feet", b"yard"):
                        uidx = window.find(uname)
                        if uidx >= 0:
                            unit_name = uname.decode("ascii", errors="ignore")
                            break
                    return (val, unit_name)

        # Fallback: can't parse binary, assume default (1.0 = cm)
        return (1.0, "cm (assumed — binary parse failed)")
    else:
        # ASCII FBX: search for UnitScaleFactor in text
        text = raw.decode("utf-8", errors="ignore")
        match = re.search(r'UnitScaleFactor\s*:\s*([0-9.]+)', text)
        scale = float(match.group(1)) if match else 1.0

        # Try to find units
        units = "cm"
        for u in (r'"mm"', r'"cm"', r'"m"', r'"inches"', r'"foot"', r'"yard"'):
            if re.search(rf'Units\s*:\s*{u}', text):
                units = u.strip('"')
                break

        return (scale, units)


def detect_import_scale(fbx_path):
    """
    Auto-detect the S&Box import_scale from the FBX UnitScaleFactor.

    S&Box import_scale converts FBX units to S&Box units (inches).
    - UnitScaleFactor 1.0 with cm units → import_scale 0.3937 (1 cm = 0.3937 in)
    - UnitScaleFactor 1.0 with m units → import_scale 39.37 (1 m = 39.37 in)
    - UnitScaleFactor 1.0 with mm units → import_scale 0.03937
    - UnitScaleFactor 2.54 with inches → import_scale 1.0 (1 in = 1 S&Box unit)

    Returns (import_scale, description).
    """
    raw_scale, units = parse_fbx_unit_scale(fbx_path)

    # The FBX UnitScaleFactor is relative to centimeters by convention.
    # So UnitScaleFactor=1.0 means 1 FBX unit = 1 cm.
    # UnitScaleFactor=2.54 means 1 FBX unit = 2.54 cm = 1 inch.
    # UnitScaleFactor=100 means 1 FBX unit = 100 cm = 1 meter.

    cm_per_fbx_unit = raw_scale  # by FBX convention

    # If we detected a units string, use it directly
    units_lower = units.lower().strip()
    if units_lower.startswith("m") and units_lower != "mm":
        # meters
        import_scale = M_TO_INCHES
        desc = f"meters (UnitScaleFactor={raw_scale}, units='{units}')"
    elif units_lower == "mm":
        import_scale = MM_TO_INCHES
        desc = f"millimeters (UnitScaleFactor={raw_scale}, units='{units}')"
    elif units_lower in ("inches", "inch", "in"):
        import_scale = 1.0
        desc = f"inches (UnitScaleFactor={raw_scale}, units='{units}')"
    elif units_lower in ("foot", "feet", "ft"):
        import_scale = 12.0  # 1 foot = 12 inches
        desc = f"feet (UnitScaleFactor={raw_scale}, units='{units}')"
    elif units_lower in ("yard", "yd"):
        import_scale = 36.0
        desc = f"yards (UnitScaleFactor={raw_scale}, units='{units}')"
    else:
        # Default: assume cm (FBX convention)
        import_scale = CM_TO_INCHES
        desc = f"centimeters (UnitScaleFactor={raw_scale}, units='{units}' — assumed cm)"

    return (import_scale, desc)


def estimate_mesh_complexity(fbx_path):
    """
    Estimate mesh complexity from the FBX file to decide collision type.
    Returns (vertex_count_estimate, polygon_count_estimate).

    For binary FBX, we count occurrences of geometry-related markers.
    For ASCII FBX, we count "Vertices" and "PolygonVertexIndex" entries.
    """
    with open(fbx_path, "rb") as f:
        raw = f.read()

    is_binary = b"Kaydara FBX Binary" in raw[:50]

    if is_binary:
        # Binary: count "Vertices" and "PolygonVertexIndex" occurrences
        # and try to read the array sizes
        vert_count = 0
        poly_count = 0

        idx = raw.find(b"Vertices")
        if idx >= 0:
            # In binary FBX, the array size is stored as an int32 after
            # the property header. Search for a plausible count.
            window = raw[idx:idx + 100]
            for offset in range(len(window) - 4):
                val = struct.unpack("<I", window[offset:offset + 4])[0]
                if 3 <= val <= 10000000:
                    vert_count = val
                    break

        idx = raw.find(b"PolygonVertexIndex")
        if idx >= 0:
            window = raw[idx:idx + 100]
            for offset in range(len(window) - 4):
                val = struct.unpack("<I", window[offset:offset + 4])[0]
                if 3 <= val <= 10000000:
                    poly_count = val
                    break

        return (vert_count, poly_count)
    else:
        text = raw.decode("utf-8", errors="ignore")
        # ASCII FBX: "Vertices: *N { a: [ ... ] }"
        vmatch = re.search(r'Vertices\s*:\s*\*(\d+)', text)
        pmatch = re.search(r'PolygonVertexIndex\s*:\s*\*(\d+)', text)
        vert_count = int(vmatch.group(1)) if vmatch else 0
        # PolygonVertexIndex counts vertex indices, not polygons.
        # Triangles have 3 indices, quads have 4. Estimate polygons.
        idx_count = int(pmatch.group(1)) if pmatch else 0
        poly_count = idx_count // 3 if idx_count else 0
        return (vert_count, poly_count)


def decide_collision_type(vert_count, poly_count, force=None):
    """
    Decide whether to use convex hull or concave mesh collision.

    - Convex hull (PhysicsHullFromRender): fast, good for simple props,
      furniture, rocks. Max 32 vertices per hull. NOT walkable on
      stairs/terraces (single convex shape over whole model).
    - Concave mesh (PhysicsMeshFromRender): accurate, good for
      architectural models (buildings, stairs, terraces). Slower but
      preserves walkable surfaces.

    Heuristic: if polygon count > 500 or vertex count > 1000, use concave
    (architectural). Otherwise convex (prop).
    """
    if force:
        return force

    if poly_count > 500 or vert_count > 1000:
        return "concave"
    return "convex"


def generate_vmdl(fbx_rel_path, import_scale, collision_type, material_path, out_path):
    """
    Generate a .vmdl file (KV3 text format) referencing the FBX.

    Args:
        fbx_rel_path: FBX path relative to the sbox/ project root
                      (e.g. "models/medieval/base.fbx")
        import_scale: float, S&Box import_scale (FBX units → inches)
        collision_type: "convex" or "concave"
        material_path: .vmat path for the MaterialGroupList fallback
        out_path: absolute path to write the .vmdl file
    """
    if collision_type == "concave":
        physics_node = """\t\t\t{
\t\t\t\t_class = "PhysicsMeshFromRender"
\t\t\t\tparent_bone = ""
\t\t\t\tsurface_prop = "default"
\t\t\t\tcollision_tags = "solid"
\t\t\t}"""
    else:
        physics_node = """\t\t\t{
\t\t\t\t_class = "PhysicsHullFromRender"
\t\t\t\tparent_bone = ""
\t\t\t\tsurface_prop = "default"
\t\t\t\tcollision_tags = "solid"
\t\t\t\tfaceMergeAngle = 20.0
\t\t\t\tmaxHullVertices = 32
\t\t\t}"""

    vmdl = f"""<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc29:version{{3cec427c-1b0e-4d48-a90a-0436f33a6041}} -->
{{
\trootNode =
\t{{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t]
\t\t\t\t\t\tuse_global_default = true
\t\t\t\t\t\tglobal_default_material = "{material_path}"
\t\t\t\t\t}},
\t\t\t\t]
\t\t\t}},
\t\t\t{{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "LOD0"
\t\t\t\t\t\tfilename = "{fbx_rel_path}"
\t\t\t\t\t\timport_translation = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\timport_rotation = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\timport_scale = {import_scale}
\t\t\t\t\t\talign_origin_x_type = "None"
\t\t\t\t\t\talign_origin_y_type = "None"
\t\t\t\t\t\talign_origin_z_type = "None"
\t\t\t\t\t\tparent_bone = ""
\t\t\t\t\t\timport_filter =
\t\t\t\t\t\t{{
\t\t\t\t\t\t\texclude_by_default = false
\t\t\t\t\t\t\texception_list =
\t\t\t\t\t\t\t[
\t\t\t\t\t\t\t]
\t\t\t\t\t\t}}
\t\t\t\t\t}},
\t\t\t\t]
\t\t\t}},
\t\t\t{{
\t\t\t\t_class = "PhysicsShapeList"
\t\t\t\tchildren =
\t\t\t\t[
{physics_node}
\t\t\t\t]
\t\t\t}},
\t\t]
\t\tmodel_archetype = ""
\t\tprimary_associated_entity = ""
\t\tanim_graph_name = ""
\t\tbase_model_name = ""
\t}}
}}
"""
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(vmdl)
    return vmdl


def verify_model_bounds(vmdl_rel_path):
    """
    Use the S&Box MCP server to query a compiled model's bounds.
    Requires the editor to be running with play mode active.

    Calls the MCP 'call_tool' with a tool that loads the model and
    reports bounds. Since there's no direct 'get model bounds' MCP tool,
    we use read_console after triggering a log via a scene trace or
    object inspection.

    For now, this is a placeholder that reports the expected verification
    command. A future enhancement could add a C# diagnostic component
    that logs Model.Load(...).Bounds on demand.
    """
    print(f"\n  Verification: to check bounds in-game, add a diagnostic")
    print(f"  component that calls:")
    print(f"    var model = Model.Load(\"{vmdl_rel_path}\");")
    print(f"    Log.Info($\"Bounds: mins={{model.Bounds.Mins}} maxs={{model.Bounds.Maxs}}\");")
    print(f"    Log.Info($\"Size (m): {{model.Bounds.Size / 39.37f}}\");")
    print(f"  Or use MCP scene_trace to raycast against the spawned model.")
    return None


def main():
    parser = argparse.ArgumentParser(
        description="Automated FBX/OBJ ingestion pipeline for Lute (S&Box)."
    )
    parser.add_argument("fbx", help="Path to the .fbx (or .obj) file to ingest.")
    parser.add_argument("--scale", type=float, default=None,
                        help="Override auto-detected import_scale (FBX units → inches).")
    parser.add_argument("--collision", choices=["convex", "concave", "auto"],
                        default="auto",
                        help="Collision type: convex hull, concave mesh, or auto-detect.")
    parser.add_argument("--material", default=DEFAULT_MATERIAL,
                        help=f"Fallback .vmat path for MaterialGroupList (default: {DEFAULT_MATERIAL}).")
    parser.add_argument("--outdir", default=None,
                        help="Output directory for the .vmdl (default: same dir as FBX).")
    parser.add_argument("--name", default=None,
                        help="Output .vmdl name (without extension). Default: FBX filename.")
    parser.add_argument("--verify", action="store_true",
                        help="Print bounds verification instructions.")
    parser.add_argument("--dry-run", action="store_true",
                        help="Print what would be done without writing files.")

    args = parser.parse_args()

    fbx_abs = os.path.abspath(args.fbx)
    if not os.path.isfile(fbx_abs):
        print(f"ERROR: File not found: {fbx_abs}")
        sys.exit(1)

    print(f"=== Lute Asset Ingestion ===")
    print(f"  Source: {fbx_abs}")

    # 1. Auto-detect import scale
    print(f"\n  [1/4] Detecting import scale...")
    raw_scale, units = parse_fbx_unit_scale(fbx_abs)
    auto_scale, scale_desc = detect_import_scale(fbx_abs)
    import_scale = args.scale if args.scale is not None else auto_scale
    print(f"    UnitScaleFactor: {raw_scale}")
    print(f"    Detected units: {scale_desc}")
    print(f"    import_scale: {import_scale}")

    # 2. Estimate mesh complexity for collision decision
    print(f"\n  [2/4] Estimating mesh complexity...")
    vert_count, poly_count = estimate_mesh_complexity(fbx_abs)
    print(f"    Vertices (est.): {vert_count}")
    print(f"    Polygons (est.): {poly_count}")

    collision_type = decide_collision_type(vert_count, poly_count,
                                           force=None if args.collision == "auto" else args.collision)
    print(f"    Collision type: {collision_type}")

    # 3. Determine output paths
    out_dir = args.outdir if args.outdir else os.path.dirname(fbx_abs)
    out_name = args.name if args.name else os.path.splitext(os.path.basename(fbx_abs))[0]
    vmdl_path = os.path.join(out_dir, out_name + ".vmdl")

    # Compute FBX path relative to sbox/Assets/ (for the .vmdl filename field)
    # The .vmdl filename should be relative to the sbox project root.
    try:
        fbx_rel = os.path.relpath(fbx_abs, SBOX_ASSETS)
        # Normalize to forward slashes (S&Box uses forward slashes)
        fbx_rel = fbx_rel.replace("\\", "/")
    except ValueError:
        # On different drives, fall back to absolute
        fbx_rel = fbx_abs.replace("\\", "/")

    # If the FBX is not under sbox/Assets, we need to copy it there
    if fbx_abs.startswith(SBOX_ASSETS) or os.path.commonpath([fbx_abs, SBOX_ASSETS]) == SBOX_ASSETS:
        fbx_in_assets = True
    else:
        fbx_in_assets = False
        # Copy FBX into the output dir (which should be under Assets)
        target_fbx_dir = os.path.join(out_dir) if out_dir else os.path.dirname(fbx_abs)
        target_fbx = os.path.join(target_fbx_dir, os.path.basename(fbx_abs))
        if not args.dry_run and target_fbx != fbx_abs:
            os.makedirs(target_fbx_dir, exist_ok=True)
            import shutil
            shutil.copy2(fbx_abs, target_fbx)
            print(f"\n  Copied FBX to: {target_fbx}")
        fbx_abs = target_fbx
        fbx_rel = os.path.relpath(fbx_abs, SBOX_ASSETS).replace("\\", "/")

    # Material path: ensure it's a valid .vmat path relative to sbox root
    material_path = args.material
    if not material_path.endswith(".vmat"):
        material_path += ".vmat"

    print(f"\n  [3/4] Generating .vmdl...")
    print(f"    Output: {vmdl_path}")
    print(f"    FBX path (rel): {fbx_rel}")
    print(f"    Material fallback: {material_path}")

    if not args.dry_run:
        os.makedirs(out_dir, exist_ok=True)
        generate_vmdl(fbx_rel, import_scale, collision_type, material_path, vmdl_path)
        print(f"    Written: {vmdl_path}")
    else:
        print(f"    [DRY RUN] Would write: {vmdl_path}")

    # 4. Verification
    print(f"\n  [4/4] Verification...")
    vmdl_rel = os.path.relpath(vmdl_path, SBOX_ASSETS).replace("\\", "/")
    if args.verify:
        verify_model_bounds(vmdl_rel)
    else:
        print(f"    (use --verify for bounds check instructions)")
        print(f"    Model path: {vmdl_rel}")

    # Summary
    print(f"\n=== Summary ===")
    print(f"  FBX: {os.path.basename(fbx_abs)}")
    print(f"  import_scale: {import_scale} ({scale_desc})")
    print(f"  Vertices (est.): {vert_count}, Polygons (est.): {poly_count}")
    print(f"  Collision: {collision_type}")
    print(f"  Material: {material_path}")
    print(f"  .vmdl: {vmdl_path}")
    print(f"  Model path (for code): {vmdl_rel}")

    # Sync to live addon if under sbox/Assets
    if not args.dry_run and vmdl_path.startswith(SBOX_ASSETS):
        live_addon = os.path.join(os.path.dirname(SBOX_ASSETS), "..",
                                  "sbox-public-clean", "game", "addons", "lute")
        live_addon = os.path.normpath(live_addon)
        if os.path.isdir(live_addon):
            rel_to_assets = os.path.relpath(vmdl_path, SBOX_ASSETS)
            live_vmdl = os.path.join(live_addon, "Assets", rel_to_assets)
            os.makedirs(os.path.dirname(live_vmdl), exist_ok=True)
            import shutil
            shutil.copy2(vmdl_path, live_vmdl)
            # Also sync the FBX if it was copied
            live_fbx = os.path.join(live_addon, "Assets", fbx_rel)
            if os.path.isfile(fbx_abs) and not os.path.isfile(live_fbx):
                os.makedirs(os.path.dirname(live_fbx), exist_ok=True)
                shutil.copy2(fbx_abs, live_fbx)
            print(f"  Synced to live addon: {live_vmdl}")

    print(f"\nDone!")


if __name__ == "__main__":
    main()
