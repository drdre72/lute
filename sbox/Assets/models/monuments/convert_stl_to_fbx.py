import bpy
import sys
import os

# Get input/output from command line args
argv = sys.argv
argv = argv[argv.index("--") + 1:]
input_stl = argv[0]
output_fbx = argv[1]

# Clear default scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# Import STL - Blender 5.x uses the io_mesh_stl addon
try:
    bpy.ops.import_mesh.stl(filepath=input_stl)
except AttributeError:
    # Try the newer API
    import addon_utils
    addon_utils.enable("io_mesh_stl", default_set=True)
    bpy.ops.import_mesh.stl(filepath=input_stl)

# Select the imported object
obj = bpy.context.active_object

# Apply a simple material so the FBX has at least one material slot
mat = bpy.data.materials.new(name="acropolis_stone")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.5, 0.5, 0.5, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.8
obj.data.materials.append(mat)

# Export as FBX
bpy.ops.export_scene.fbx(
    filepath=output_fbx,
    use_selection=True,
    apply_unit_scale=True,
    axis_forward='Y',
    axis_up='Z'
)

print(f"Converted {input_stl} -> {output_fbx}")
print(f"Object dimensions: {obj.dimensions}")
print(f"Object vertex count: {len(obj.data.vertices)}")
print(f"Object face count: {len(obj.data.polygons)}")
