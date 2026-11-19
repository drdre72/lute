#!/usr/bin/env python3
"""
Import Kenney Castle Kit FBX models into S&Box Source 2 format.

Creates .vmdl files (KV3 text) for each FBX, .vtex files for textures,
and .vmat material files. Copies everything to both the repo and the
editor addon directory.

Kenney assets are authored in meters, so import_scale = 39.37 (meters to inches).
Uses global_default_material to bypass the FBX material name gotcha.
"""

import os
import shutil
import sys
from pathlib import Path

# Paths
SRC = Path(r"C:\Users\Shadow\Downloads\kenney_castle-kit")
REPO = Path(r"D:\Documents\lute-main\lute-main\sbox\Assets")
ADDON = Path(os.environ.get("LUTE_ADDON_ROOT", r"D:\Documents\lute-main\lute-main\sbox")) / "Assets"

# Asset subdirectories
MODEL_DIR = "models/castle_kit"
MAT_DIR = "materials/castle_kit"
TEX_DIR = "materials/castle_kit"

# Import scale: Kenney assets are in meters, S&Box uses inches
# 1 meter = 39.37 inches
IMPORT_SCALE = "39.37"

# Default material to use (bypasses FBX material name issues)
DEFAULT_MATERIAL = "materials/dev/primary_white.vmat"


def ensure_dir(path: Path):
    path.mkdir(parents=True, exist_ok=True)


def _dest_dirs(subdir: str) -> list[Path]:
    """Return unique destination dirs for a subdir under each Assets root."""
    return list({(base / subdir).resolve() for base in {REPO, ADDON}})


def _copy(src: Path, dst_dir: Path):
    """Copy a file, clearing any read-only flag the source carried."""
    dst = dst_dir / src.name
    shutil.copyfile(src, dst)
    os.chmod(dst, 0o666)


def copy_fbx_files():
    """Copy FBX files to the asset directories (deduped — repo == addon now)."""
    fbx_src = SRC / "Models" / "FBX format"
    if not fbx_src.exists():
        print(f"ERROR: FBX source not found: {fbx_src}")
        return []

    fbx_files = list(fbx_src.glob("*.fbx"))
    print(f"Found {len(fbx_files)} FBX files")

    dest_dirs = _dest_dirs(MODEL_DIR)
    for d in dest_dirs:
        ensure_dir(d)

    copied = []
    for fbx in fbx_files:
        for d in dest_dirs:
            _copy(fbx, d)
        copied.append(fbx.stem)

    print(f"Copied {len(copied)} FBX files to {len(dest_dirs)} asset dir(s)")
    return copied


def copy_textures():
    """Copy texture PNGs to the asset directories."""
    tex_src = SRC / "Models" / "FBX format" / "Textures"
    if not tex_src.exists():
        tex_src = SRC / "Models" / "Textures"

    if not tex_src.exists():
        print("WARNING: No textures directory found")
        return []

    tex_files = list(tex_src.glob("*.png"))
    print(f"Found {len(tex_files)} texture files")

    dest_dirs = _dest_dirs(TEX_DIR)
    for d in dest_dirs:
        ensure_dir(d)

    copied = []
    for tex in tex_files:
        for d in dest_dirs:
            _copy(tex, d)
        copied.append(tex.stem)

    print(f"Copied {len(copied)} textures to {len(dest_dirs)} asset dir(s)")
    return copied


def create_vtex(tex_name: str):
    """Create a .vtex file for a texture PNG in DMX keyvalues2 format.

    The legacy `/* VTEX */` JSON shape fails with 'Error reading texture
    compile settings'. The engine's canonical format is
    `<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->` + CDmeVtex.
    """
    vtex_content = f"""<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{{
	"m_inputTextureArray" "element_array" 
	[
		"CDmeInputTexture"
		{{
			"m_name" "string" "SheetTexture"
			"m_fileName" "string" "materials/castle_kit/{tex_name}.png"
			"m_colorSpace" "string" "sRGB"
			"m_typeString" "string" "2D"
			"m_imageProcessorArray" "element_array" 
			[
			]
		}}
	]
	"m_outputTypeString" "string" "2D"
	"m_outputFormat" "string" "DXT5"
	"m_outputClearColor" "vector4" "0 0 0 0"
	"m_nOutputMinDimension" "int" "0"
	"m_nOutputMaxDimension" "int" "0"
	"m_textureOutputChannelArray" "element_array" 
	[
		"CDmeTextureOutputChannel"
		{{
			"m_inputTextureArray" "string_array" 
			[
				"SheetTexture"
			]
			"m_srcChannels" "string" "rgba"
			"m_dstChannels" "string" "rgba"
			"m_mipAlgorithm" "CDmeImageProcessor"
			{{
				"m_algorithm" "string" ""
				"m_stringArg" "string" ""
				"m_vFloat4Arg" "vector4" "0 0 0 0"
			}}
			"m_outputColorSpace" "string" "sRGB"
		}}
	]
	"m_vClamp" "vector3" "0 0 0"
	"m_bNoLod" "bool" "1"
}}
"""

    for base in {REPO.resolve(), ADDON.resolve()}:
        vtex_path = base / TEX_DIR / f"{tex_name}.vtex"
        ensure_dir(vtex_path.parent)
        vtex_path.write_text(vtex_content, encoding="utf-8")

    print(f"  Created {tex_name}.vtex")


def create_vmat(tex_name: str):
    """Create a .vmat material file referencing a colormap texture.

    Emits KV2 Layer0 format — the legacy `/* VMAT */` JSON shape no
    longer compiles.
    """
    vmat_content = f"""Layer0
{{
    shader "shaders/complex.shader"
    g_flAmbientOcclusionDirectDiffuse "1.000"
    TextureAmbientOcclusion "materials/default/default_ao.tga"
    g_flModelTintAmount "1.000"
    g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
    TextureColor "materials/castle_kit/{tex_name}.png"
    g_bFogEnabled "1"
    g_flMetalness "0.000"
    TextureNormal "materials/default/default_normal.tga"
    g_flRoughnessScaleFactor "1.000"
    TextureRoughness "materials/default/default_rough.tga"
}}
"""

    for base in {REPO.resolve(), ADDON.resolve()}:
        vmat_path = base / MAT_DIR / f"{tex_name}.vmat"
        ensure_dir(vmat_path.parent)
        vmat_path.write_text(vmat_content, encoding="utf-8")

    print(f"  Created {tex_name}.vmat")


VMDL_TEMPLATE = """<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
{
	rootNode =
	{
		_class = "RootNode"
		children =
		[
			{
				_class = "MaterialGroupList"
				children =
				[
					{
						_class = "DefaultMaterialGroup"
						remaps =
						[
						]
						use_global_default = true
						global_default_material = "{MATERIAL}"
					},
				]
			},
			{
				_class = "RenderMeshList"
				children =
				[
					{
						_class = "RenderMeshFile"
						name = "LOD0"
						filename = "models/castle_kit/{NAME}.fbx"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 39.3701
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{
							exclude_by_default = false
							exception_list =
							[
							]
						}
					},
				]
			},
			{
				_class = "PhysicsShapeList"
				children =
				[
			{
				_class = "PhysicsMeshFromRender"
				parent_bone = ""
				surface_prop = "default"
				collision_tags = "solid"
			}
				]
			},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}
}
"""


def create_vmdl(fbx_stem: str):
    """Create a .vmdl file for an FBX model in KV3 modeldoc29 format.

    The kv3 encoding header is required — plain `/* VMDL */` text fails
    with 'Unable to determine buffer encoding'. Uses
    MaterialGroupList/global_default_material to bypass the FBX material
    name gotcha (root.0.0, root.1, etc.).
    """
    vmdl_content = VMDL_TEMPLATE.replace("{NAME}", fbx_stem).replace("{MATERIAL}", DEFAULT_MATERIAL)

    for base in {REPO.resolve(), ADDON.resolve()}:
        vmdl_path = base / MODEL_DIR / f"{fbx_stem}.vmdl"
        ensure_dir(vmdl_path.parent)
        vmdl_path.write_text(vmdl_content, encoding="utf-8")

    print(f"  Created {fbx_stem}.vmdl")


def main():
    print("=== Kenney Castle Kit Import Pipeline ===")
    print()

    # Step 1: Copy FBX files
    fbx_stems = copy_fbx_files()
    if not fbx_stems:
        print("No FBX files found, aborting.")
        sys.exit(1)

    # Step 2: Copy textures
    tex_names = copy_textures()

    # Step 3: Create .vtex files for textures
    print("\nCreating .vtex files...")
    for tex in tex_names:
        create_vtex(tex)

    # Step 4: Create .vmat material files
    print("\nCreating .vmat files...")
    # Create a castle_kit material from the colormap
    if "colormap" in tex_names:
        create_vmat("castle_kit_colormap")
    for tex in tex_names:
        if tex != "colormap":
            create_vmat(f"castle_kit_{tex}")

    # Step 5: Create .vmdl files for each FBX
    print(f"\nCreating .vmdl files for {len(fbx_stems)} models...")
    for stem in fbx_stems:
        create_vmdl(stem)

    print(f"\n=== Import complete ===")
    print(f"  {len(fbx_stems)} models imported")
    print(f"  {len(tex_names)} textures imported")
    print(f"  Files in both repo and addon directories")
    print()
    print("Next: Restart S&Box editor to compile the .vmdl files.")
    print("The editor will auto-compile .vmdl -> .vmdl_c on load.")


if __name__ == "__main__":
    main()
