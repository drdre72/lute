@tool
extends EditorPlugin

var terrain_script := load("res://addons/terrain_generator/terrain_generator_node.gd")
var scatter_script := load("res://addons/terrain_generator/scatter_node.gd")

func _enter_tree() -> void:
	# عقدة التضاريس
	add_custom_type(
		"TerrainGenerator3D",
		"MeshInstance3D",
		terrain_script,
		_get_icon("MeshInstance3D")
	)

	# عقدة النثر الجديدة
	add_custom_type(
		"Scatter3D",
		"Node3D",
		scatter_script,
		_get_icon("Node3D")
	)

func _exit_tree() -> void:
	remove_custom_type("TerrainGenerator3D")
	remove_custom_type("ScatterNode3D")

func _get_icon(type_name: String) -> Texture2D:
	return get_editor_interface().get_base_control().get_theme_icon(type_name, "EditorIcons")
