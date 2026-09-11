#!/usr/bin/env python3
"""
Regenerate Kenney Castle Kit .vmdl files in correct KV3 format.
Matches the proven format from medieval/base.vmdl.
"""

import os
from pathlib import Path

REPO = Path(r"C:\Users\Shadow\Documents\lute\sbox\Assets")
ADDON = Path(r"C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute\Assets")
MODEL_DIR = "models/castle_kit"

# Use our existing stone material as default (proven to work)
DEFAULT_MATERIAL = "materials/castle_kit/castle_kit_colormap.vmat"

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

def main():
    model_dir_repo = REPO / MODEL_DIR
    model_dir_addon = ADDON / MODEL_DIR
    
    fbx_files = list(model_dir_repo.glob("*.fbx"))
    print(f"Regenerating {len(fbx_files)} .vmdl files in KV3 format...")
    
    for fbx in fbx_files:
        stem = fbx.stem
        content = VMDL_TEMPLATE.replace("{NAME}", stem).replace("{MATERIAL}", DEFAULT_MATERIAL)
        
        # Write to both repo and addon
        (model_dir_repo / f"{stem}.vmdl").write_text(content, encoding="utf-8")
        (model_dir_addon / f"{stem}.vmdl").write_text(content, encoding="utf-8")
        print(f"  {stem}.vmdl")
    
    print(f"\nDone! {len(fbx_files)} .vmdl files regenerated in KV3 format.")
    print("Next: Restart S&Box editor to trigger compilation.")

if __name__ == "__main__":
    main()
