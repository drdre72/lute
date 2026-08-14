@tool
extends MeshInstance3D

@export_category("Terrain Configuration")
@export var map_size: int = 128
@export var vertex_spacing: float = 2.0
@export var max_height: float = 35.0
@export var noise_seed: int = 1337

@export_category("Scatter")
@export var tree_density_threshold: float = 0.45
@export var tree_height_min: float = 2.0
@export var tree_slope_threshold: float = 0.85

var noise: FastNoiseLite = FastNoiseLite.new()
var _heights: PackedFloat32Array = PackedFloat32Array()
var _normals: PackedVector3Array = PackedVector3Array()

func _ready() -> void:
	generate_terrain()

func generate_terrain() -> void:
	noise.seed = noise_seed
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX
	noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	noise.frequency = 0.008

	var st = SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	var half_size = (map_size * vertex_spacing) / 2.0
	_heights.resize(map_size * map_size)
	_normals.resize(map_size * map_size)

	# Generate Vertices, UVs
	for z in range(map_size):
		for x in range(map_size):
			var world_x = (x * vertex_spacing) - half_size
			var world_z = (z * vertex_spacing) - half_size

			# Radial Distance Mask (0 at center, 1 at edge)
			var nx = (float(x) / map_size) * 2.0 - 1.0
			var nz = (float(z) / map_size) * 2.0 - 1.0
			var dist = sqrt(nx * nx + nz * nz)
			var island_mask = clamp(1.0 - pow(dist, 2.0), 0.0, 1.0)

			# Sample Noise + Apply Mask
			var raw_noise = (noise.get_noise_2d(world_x, world_z) + 1.0) / 2.0
			var height = raw_noise * max_height * island_mask

			_heights[z * map_size + x] = height

			st.set_uv(Vector2(float(x) / map_size, float(z) / map_size))
			st.add_vertex(Vector3(world_x, height, world_z))

	# Construct Triangles
	for z in range(map_size - 1):
		for x in range(map_size - 1):
			var i = z * map_size + x
			st.add_index(i)
			st.add_index(i + map_size)
			st.add_index(i + 1)

			st.add_index(i + 1)
			st.add_index(i + map_size)
			st.add_index(i + map_size + 1)

	st.generate_normals()
	st.generate_tangents()
	mesh = st.commit()

	_compute_normals()
	_apply_shader()
	_generate_collisions()
	_scatter_trees()

func _compute_normals() -> void:
	for z in range(map_size):
		for x in range(map_size):
			var i = z * map_size + x
			var h = _heights[i]
			var h_l = _heights[i - 1] if x > 0 else h
			var h_r = _heights[i + 1] if x < map_size - 1 else h
			var h_u = _heights[i - map_size] if z > 0 else h
			var h_d = _heights[i + map_size] if z < map_size - 1 else h
			var n = Vector3(h_l - h_r, 2.0 * vertex_spacing, h_u - h_d).normalized()
			_normals[i] = n

func _apply_shader() -> void:
	var shader = load("res://shaders/terrain.gdshader")
	if shader == null:
		push_warning("Terrain shader not found")
		return
	var mat = ShaderMaterial.new()
	mat.shader = shader

	# Load PBR textures with error checking
	var tex_base = "res://textures/pbr_final/"
	var tex_names = {
		"sand_color_tex": "sand_color.jpg",
		"sand_normal_tex": "sand_normal.jpg",
		"sand_roughness_tex": "sand_roughness.jpg",
		"grass_color_tex": "grass_color.jpg",
		"grass_normal_tex": "grass_normal.jpg",
		"grass_roughness_tex": "grass_roughness.jpg",
		"rock_color_tex": "rock_color.jpg",
		"rock_normal_tex": "rock_normal.jpg",
		"rock_roughness_tex": "rock_roughness.jpg",
	}
	for param_name in tex_names:
		var tex_path = tex_base + tex_names[param_name]
		var tex = load(tex_path)
		if tex == null:
			push_warning("Failed to load texture: " + tex_path)
		else:
			mat.set_shader_parameter(param_name, tex)

	mat.set_shader_parameter("texture_scale", 32.0)
	set_surface_override_material(0, mat)

func _generate_collisions() -> void:
	for child in get_children():
		if child is StaticBody3D:
			child.queue_free()
	create_trimesh_collision()

func _scatter_trees() -> void:
	for child in get_children():
		if child is MultiMeshInstance3D:
			child.queue_free()

	var positions: Array[Vector3] = []
	var half_size = (map_size * vertex_spacing) / 2.0

	for z in range(2, map_size - 2):
		for x in range(2, map_size - 2):
			var i = z * map_size + x
			var h = _heights[i]
			var n = _normals[i]
			if h > tree_height_min and n.y > tree_slope_threshold:
				var noise_val = (noise.get_noise_2d(float(x) * 3.0, float(z) * 3.0) + 1.0) / 2.0
				if noise_val > tree_density_threshold:
					var world_x = (x * vertex_spacing) - half_size
					var world_z = (z * vertex_spacing) - half_size
					positions.append(Vector3(world_x, h, world_z))

	if positions.is_empty():
		return

	var tree_mesh = CylinderMesh.new()
	tree_mesh.top_radius = 0.0
	tree_mesh.bottom_radius = 0.3
	tree_mesh.height = 3.0
	var tree_mat = StandardMaterial3D.new()
	tree_mat.albedo_color = Color(0.15, 0.4, 0.1, 1.0)
	tree_mat.roughness = 0.9
	tree_mesh.material = tree_mat

	var mm = MultiMesh.new()
	mm.mesh = tree_mesh
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.instance_count = positions.size()

	var rng = RandomNumberGenerator.new()
	rng.seed = noise_seed
	for idx in range(positions.size()):
		var pos = positions[idx]
		var t = Transform3D()
		var s = rng.randf_range(0.7, 1.3)
		t = t.scaled(Vector3(s, s, s))
		t = t.rotated(Vector3.UP, rng.randf_range(0, TAU))
		t.origin = pos
		mm.set_instance_transform(idx, t)

	var mmi = MultiMeshInstance3D.new()
	mmi.name = "TreeScatter"
	mmi.multimesh = mm
	add_child(mmi)
	if get_tree() and get_tree().get_edited_scene_root():
		mmi.owner = get_tree().get_edited_scene_root()
