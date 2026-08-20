@tool
extends MeshInstance3D
class_name TerrainGenerator3D

@export_group("Terrain Size")
@export var width: int = 64:
	set(val):
		width = max(val, 2)
		if auto_update: generate_terrain()

@export var depth: int = 64:
	set(val):
		depth = max(val, 2)
		if auto_update: generate_terrain()

@export var height_scale: float = 10.0:
	set(val):
		height_scale = val
		if auto_update: generate_terrain()

@export_group("Noise Settings")
@export var noise: FastNoiseLite:
	set(val):
		noise = val
		if noise and not noise.is_connected("changed", Callable(self, "_on_noise_changed")):
			noise.changed.connect(_on_noise_changed)
		if auto_update: generate_terrain()

@export_group("Generation")
@export var auto_update: bool = true
@export var generate_button: bool = false:
	set(val):
		if val:
			generate_terrain()
			generate_button = false

@export var add_collision_button: bool = false:
	set(val):
		if val:
			_add_collision()
			add_collision_button = false

@export var add_scatter_collision_button: bool = false:
	set(val):
		if val:
			_add_scatter_collision()
			add_scatter_collision_button = false

var static_body := StaticBody3D.new()
var scatter_body := StaticBody3D.new()

func _ready() -> void:

	if noise == null:
		noise = FastNoiseLite.new()
		noise.frequency = 0.03
		noise.noise_type = FastNoiseLite.TYPE_PERLIN
		noise.changed.connect(_on_noise_changed)

	generate_terrain()

func _on_noise_changed() -> void:
	if auto_update:
		generate_terrain()

func generate_terrain() -> void:
	if noise == null or width < 2 or depth < 2:
		return

	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	for z in range(depth):
		for x in range(width):
			var h := noise.get_noise_2d(x, z) * height_scale
			var vx := x - width / 2.0
			var vz := z - depth / 2.0

			st.set_uv(Vector2(x / float(width), z / float(depth)))
			st.add_vertex(Vector3(vx, h, vz))

	for z in range(depth - 1):
		for x in range(width - 1):
			var tl := z * width + x
			var tr := tl + 1
			var bl := (z + 1) * width + x
			var br := bl + 1

			st.add_index(tl)
			st.add_index(bl)
			st.add_index(tr)

			st.add_index(tr)
			st.add_index(bl)
			st.add_index(br)

	st.generate_normals()
	st.generate_tangents()

	mesh = st.commit()

func _add_collision() -> void:
	for child in get_children():
		child.queue_free()
	add_child(static_body)
	if mesh == null:
		return

	for child in static_body.get_children():
		if child is CollisionShape3D:
			child.queue_free()

	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(mesh.get_faces())
	shape.backface_collision = true

	var col := CollisionShape3D.new()
	col.shape = shape
	static_body.add_child(col)
	col.owner = owner

func _add_scatter_collision() -> void:
	# حذف أي collision سابق للسكتر
	for child in scatter_body.get_children():
		if child is CollisionShape3D:
			child.queue_free()

	# نصنع BoxCollision يغطي التضاريس
	var box := BoxShape3D.new()
	box.size = Vector3(width, height_scale * 2.0, depth)

	var col := CollisionShape3D.new()
	col.shape = box
	col.global_transform = global_transform

	scatter_body.add_child(col)
	col.owner = owner
