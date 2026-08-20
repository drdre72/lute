@tool
extends Node3D
class_name Scatter3D

@export_group("Scatter Settings")
@export var scenes_to_scatter: Array[PackedScene] = []
@export var count: int = 100
@export var area_size: Vector2 = Vector2(30.0, 30.0)

@export_group("Randomization")
@export var random_rotation: bool = true
@export var random_scale: bool = true
@export var min_scale: float = 0.8
@export var max_scale: float = 1.4

@export_group("Generation")
@export var clear_before_generate: bool = true
@export var generate_button: bool = false:
	set(val):
		if val:
			generate_scatter()
			generate_button = false

func generate_scatter() -> void:
	if scenes_to_scatter.is_empty():
		push_warning("Scatter3D: No scenes assigned.")
		return

	if get_world_3d() == null:
		push_warning("Scatter3D: No world_3d available. Open the scene.")
		return

	if clear_before_generate:
		_clear_old_instances()

	var space := get_world_3d().direct_space_state

	for i in count:
		var lx := randf_range(-area_size.x / 2.0, area_size.x / 2.0)
		var lz := randf_range(-area_size.y / 2.0, area_size.y / 2.0)

		var origin_local := Vector3(lx, 200.0, lz)
		var end_local := Vector3(lx, -200.0, lz)

		var ray_origin := global_transform * origin_local
		var ray_end := global_transform * end_local

		var params := PhysicsRayQueryParameters3D.new()
		params.from = ray_origin
		params.to = ray_end
		params.collide_with_areas = true
		params.collide_with_bodies = true

		var hit := space.intersect_ray(params)

		if hit.is_empty():
			continue

		var pos = hit.position

		# اختر Scene عشوائي
		var scene := scenes_to_scatter.pick_random()
		if scene == null:
			continue

		var inst = scene.instantiate()
		if inst == null:
			continue

		inst.global_position = pos

		if random_rotation:
			inst.rotation.y = randf_range(0.0, TAU)

		if random_scale:
			var s := randf_range(min_scale, max_scale)
			inst.scale = Vector3(s, s, s)

		add_child(inst)
		inst.owner = owner

func _clear_old_instances() -> void:
	for child in get_children():
		child.queue_free()
