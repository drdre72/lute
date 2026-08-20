@tool
extends EditorPlugin

const AIPipelineServer = preload("res://addons/ai_pipeline/server.gd")
const AIPipelineDockScene = preload("res://addons/ai_pipeline/dock.tscn")

const DEFAULT_PORT := 6400

var server
var dock: Control
var undo_redo: EditorUndoRedoManager

# Custom types registered by other plugins via add_custom_type().
# Populated in _enter_tree by loading known plugin scripts.
var _custom_types: Dictionary = {}

func _register_custom_types() -> void:
	# Terrain Generator
	var tg_script := load("res://addons/terrain_generator/terrain_generator_node.gd")
	if tg_script:
		_custom_types["TerrainGenerator3D"] = {"base_type": "MeshInstance3D", "script": tg_script}
	var sc_script := load("res://addons/terrain_generator/scatter_node.gd")
	if sc_script:
		_custom_types["Scatter3D"] = {"base_type": "Node3D", "script": sc_script}
	# Open World Database
	var owdb_script := load("res://addons/open-world-database/src/open_world_database.gd")
	if owdb_script:
		_custom_types["OpenWorldDatabase"] = {"base_type": "Node", "script": owdb_script}
	var owdb_pos_script := load("res://addons/open-world-database/src/OWDBPosition.gd")
	if owdb_pos_script:
		_custom_types["OWDBPosition"] = {"base_type": "Node3D", "script": owdb_pos_script}


func _enter_tree() -> void:
	undo_redo = get_undo_redo()
	_register_custom_types()

	server = AIPipelineServer.new()
	server.plugin = self

	dock = AIPipelineDockScene.instantiate()
	dock.server = server
	if dock.has_method("set_plugin"):
		dock.set_plugin(self)
	add_control_to_bottom_panel(dock, "AI Pipeline")

	var ok: bool = server.start(DEFAULT_PORT)
	_log("Plugin loaded. Server %s on port %d." % ["started" if ok else "FAILED to start", DEFAULT_PORT])


func _exit_tree() -> void:
	if server:
		server.stop()
	if dock:
		remove_control_from_bottom_panel(dock)
		dock.queue_free()


func _process(_delta: float) -> void:
	if server:
		server.poll()


# ---------------------------------------------------------------------------
# RPC dispatch
# ---------------------------------------------------------------------------

func handle_rpc(data: Dictionary) -> Dictionary:
	var tool_name: String = data.get("tool", "")
	var args = data.get("args", {})
	if typeof(args) != TYPE_DICTIONARY:
		args = {}

	_log("RPC: %s %s" % [tool_name, JSON.stringify(args)])

	match tool_name:
		"ping":
			return {"ok": true}
		"get_scene_tree":
			return tool_get_scene_tree(args)
		"scene_create":
			return tool_scene_create(args)
		"scene_open":
			return tool_scene_open(args)
		"scene_save":
			return tool_scene_save(args)
		"node_add":
			return tool_node_add(args)
		"node_delete":
			return tool_node_delete(args)
		"node_set_property":
			return tool_node_set_property(args)
		"node_get_properties":
			return tool_node_get_properties(args)
		"script_write":
			return tool_script_write(args)
		"script_read":
			return tool_script_read(args)
		"script_attach":
			return tool_script_attach(args)
		"project_setting_get":
			return tool_project_setting_get(args)
		"project_setting_set":
			return tool_project_setting_set(args)
		"play_scene":
			return tool_play_scene(args)
		"stop_scene":
			return tool_stop_scene(args)
		"list_dir":
			return tool_list_dir(args)
		"screenshot":
			return tool_screenshot(args)
		"node_find":
			return tool_node_find(args)
		"node_call_method":
			return tool_node_call_method(args)
		"scene_stats":
			return tool_scene_stats(args)
		"material_report":
			return tool_material_report(args)
		"screenshot_camera":
			return tool_screenshot_camera(args)
		_:
			return {"error": "unknown tool: %s" % tool_name}


# ---------------------------------------------------------------------------
# Scene tools
# ---------------------------------------------------------------------------

func tool_get_scene_tree(_args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	return {"ok": true, "tree": _dump_node(root, root)}


func _dump_node(node: Node, root: Node) -> Dictionary:
	var children := []
	for child in node.get_children():
		children.append(_dump_node(child, root))
	var rel_path := "." if node == root else str(root.get_path_to(node))
	return {
		"name": node.name,
		"type": node.get_class(),
		"path": rel_path,
		"children": children,
	}


func tool_scene_create(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	var root_type: String = args.get("root_type", "Node")
	if path == "":
		return {"error": "missing path"}
	if not ClassDB.class_exists(root_type):
		return {"error": "unknown class: %s" % root_type}

	var root: Node = ClassDB.instantiate(root_type)
	root.name = path.get_file().get_basename()

	var dir := path.get_base_dir()
	if dir != "":
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dir))

	var packed := PackedScene.new()
	var pack_err := packed.pack(root)
	root.queue_free()
	if pack_err != OK:
		return {"error": "pack failed: %d" % pack_err}

	var save_err := ResourceSaver.save(packed, path)
	if save_err != OK:
		return {"error": "save failed: %d" % save_err}

	EditorInterface.get_resource_filesystem().scan()
	EditorInterface.open_scene_from_path(path)
	return {"ok": true, "path": path}


func tool_scene_open(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	if not FileAccess.file_exists(path):
		return {"error": "scene not found: %s" % path}
	EditorInterface.open_scene_from_path(path)
	return {"ok": true}


func tool_scene_save(_args: Dictionary) -> Dictionary:
	# NOTE: this RPC is dispatched synchronously from _process() (via
	# server.poll()). Calling EditorInterface.save_scene() directly from
	# here re-enters Main::iteration() through ProgressDialog's progress
	# bar update, which re-runs NOTIFICATION_PROCESS on all @tool plugins
	# (including this one) before the outer _process() call has returned.
	# That reentrancy is a known Godot editor crash
	# (https://github.com/godotengine/godot/issues/118544). Deferring the
	# call runs it on the next idle frame, outside the _process() stack,
	# avoiding the crash.
	#
	# Deferring still logs harmless ProgressDialog errors ("Do not use
	# progress dialog (task) while flushing the message queue or using
	# call_deferred()!") because the dialog rejects starting a task while
	# the deferred-call queue is being flushed -- but the save itself still
	# completes correctly regardless (verified: saved properties persist to
	# disk despite the errors).
	#
	# We deliberately do NOT bypass EditorInterface.save_scene() by packing
	# and calling ResourceSaver.save() directly (tried this first): it
	# avoids the errors, but writes to disk without going through the
	# editor's own save bookkeeping, which makes Godot think the currently
	# open scene was modified externally and pop a blocking "Files have
	# been modified outside Godot" modal on the next focus/scan -- there is
	# no API or setting to suppress this for .tscn files (unlike .gd
	# scripts, which have an auto-reload-on-external-change setting). A
	# blocking modal is worse than cosmetic error-log noise for an
	# autonomous pipeline, so the deferred call_deferred approach is kept.
	EditorInterface.call_deferred("save_scene")
	return {"ok": true}


# ---------------------------------------------------------------------------
# Node tools
# ---------------------------------------------------------------------------

func _find_node(path: String) -> Node:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return null
	if path == "" or path == "." or path == "/" or path == root.name:
		return root
	var node := root.get_node_or_null(NodePath(path))
	if node == null:
		node = root.get_node_or_null(NodePath("./" + path))
	return node


func tool_node_add(args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	var parent := _find_node(args.get("parent_path", ""))
	if parent == null:
		return {"error": "parent not found"}
	var type_name: String = args.get("type", "")
	var node: Node = null
	if ClassDB.class_exists(type_name):
		node = ClassDB.instantiate(type_name)
	elif _custom_types.has(type_name):
		var ct = _custom_types[type_name]
		node = ClassDB.instantiate(ct.base_type)
		if node and ct.script:
			node.set_script(ct.script)
	if node == null:
		return {"error": "unknown or could not instantiate: %s" % type_name}
	node.name = args.get("name", type_name)

	undo_redo.create_action("AI: add %s" % type_name)
	undo_redo.add_do_method(self, "_do_add_node", parent, node, root)
	undo_redo.add_undo_method(self, "_do_remove_node", node)
	undo_redo.commit_action()

	return {"ok": true, "path": str(root.get_path_to(node))}


func _do_add_node(parent: Node, node: Node, owner: Node) -> void:
	parent.add_child(node)
	node.owner = owner


func _do_remove_node(node: Node) -> void:
	if node.get_parent():
		node.get_parent().remove_child(node)


func tool_node_delete(args: Dictionary) -> Dictionary:
	var node := _find_node(args.get("node_path", ""))
	if node == null:
		return {"error": "node not found"}
	if node == EditorInterface.get_edited_scene_root():
		return {"error": "cannot delete scene root"}
	var parent := node.get_parent()
	if parent == null:
		return {"error": "node has no parent"}

	undo_redo.create_action("AI: delete %s" % node.name)
	undo_redo.add_do_method(parent, "remove_child", node)
	undo_redo.add_undo_method(parent, "add_child", node)
	undo_redo.commit_action()

	return {"ok": true}


func tool_node_set_property(args: Dictionary) -> Dictionary:
	var node := _find_node(args.get("node_path", ""))
	if node == null:
		return {"error": "node not found"}
	var prop: String = args.get("property", "")
	if prop == "":
		return {"error": "missing property"}

	var current = node.get(prop)
	var new_value = _coerce_value(current, args.get("value"))

	undo_redo.create_action("AI: set %s.%s" % [node.name, prop])
	undo_redo.add_do_property(node, prop, new_value)
	undo_redo.add_undo_property(node, prop, current)
	undo_redo.commit_action()

	return {"ok": true}


func tool_node_get_properties(args: Dictionary) -> Dictionary:
	var node := _find_node(args.get("node_path", ""))
	if node == null:
		return {"error": "node not found"}
	var props := {}
	for p in node.get_property_list():
		if int(p.usage) & PROPERTY_USAGE_EDITOR == 0:
			continue
		if p.name == "":
			continue
		props[p.name] = _json_safe(node.get(p.name))
	return {"ok": true, "properties": props}


func _coerce_value(current, new_value):
	if typeof(new_value) == TYPE_STRING and new_value.begins_with("res://"):
		var loaded = load(new_value)
		if loaded != null:
			return loaded
	if typeof(new_value) == TYPE_DICTIONARY and new_value.has("class"):
		var cls: String = new_value["class"]
		if ClassDB.class_exists(cls) and ClassDB.is_parent_class(cls, "Resource"):
			var res: Resource = ClassDB.instantiate(cls)
			for k in new_value.keys():
				if k == "class":
					continue
				if res is ShaderMaterial and k != "shader":
					res.set_shader_parameter(k, _coerce_value(null, new_value[k]))
				else:
					res.set(k, _coerce_value(res.get(k), new_value[k]))
			return res
	if typeof(current) == TYPE_VECTOR3 and typeof(new_value) == TYPE_DICTIONARY:
		return Vector3(new_value.get("x", 0), new_value.get("y", 0), new_value.get("z", 0))
	if typeof(current) == TYPE_VECTOR2 and typeof(new_value) == TYPE_DICTIONARY:
		return Vector2(new_value.get("x", 0), new_value.get("y", 0))
	if typeof(current) == TYPE_COLOR and typeof(new_value) == TYPE_DICTIONARY:
		return Color(new_value.get("r", 0), new_value.get("g", 0), new_value.get("b", 0), new_value.get("a", 1))
	return new_value


func _json_safe(value):
	match typeof(value):
		TYPE_INT, TYPE_FLOAT, TYPE_BOOL, TYPE_STRING, TYPE_NIL, TYPE_ARRAY, TYPE_DICTIONARY:
			return value
		TYPE_VECTOR2:
			return {"x": value.x, "y": value.y}
		TYPE_VECTOR3:
			return {"x": value.x, "y": value.y, "z": value.z}
		TYPE_COLOR:
			return {"r": value.r, "g": value.g, "b": value.b, "a": value.a}
		_:
			return str(value)


# ---------------------------------------------------------------------------
# Script tools
# ---------------------------------------------------------------------------

func tool_script_write(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	var content: String = args.get("content", "")
	if path == "":
		return {"error": "missing path"}

	var dir := path.get_base_dir()
	if dir != "":
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dir))

	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return {"error": "could not open for write: %s" % path}
	file.store_string(content)
	file.close()

	EditorInterface.get_resource_filesystem().scan()
	return {"ok": true, "path": path}


func tool_script_read(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	if not FileAccess.file_exists(path):
		return {"error": "not found: %s" % path}
	var file := FileAccess.open(path, FileAccess.READ)
	var content := file.get_as_text()
	file.close()
	return {"ok": true, "content": content}


func tool_script_attach(args: Dictionary) -> Dictionary:
	var node := _find_node(args.get("node_path", ""))
	if node == null:
		return {"error": "node not found"}
	var script_path: String = args.get("script_path", "")
	if not ResourceLoader.exists(script_path):
		return {"error": "script not found: %s" % script_path}
	var script: Script = load(script_path)
	var previous := node.get_script()

	undo_redo.create_action("AI: attach script to %s" % node.name)
	undo_redo.add_do_method(node, "set_script", script)
	undo_redo.add_undo_method(node, "set_script", previous)
	undo_redo.commit_action()

	return {"ok": true}


# ---------------------------------------------------------------------------
# Project / runtime tools
# ---------------------------------------------------------------------------

func tool_project_setting_get(args: Dictionary) -> Dictionary:
	var key: String = args.get("key", "")
	if not ProjectSettings.has_setting(key):
		return {"error": "no such setting: %s" % key}
	return {"ok": true, "value": _json_safe(ProjectSettings.get_setting(key))}


func tool_project_setting_set(args: Dictionary) -> Dictionary:
	var key: String = args.get("key", "")
	if key == "":
		return {"error": "missing key"}
	ProjectSettings.set_setting(key, args.get("value"))
	ProjectSettings.save()
	return {"ok": true}


func tool_play_scene(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	if path == "":
		EditorInterface.play_main_scene()
	else:
		EditorInterface.play_custom_scene(path)
	return {"ok": true}


func tool_stop_scene(_args: Dictionary) -> Dictionary:
	EditorInterface.stop_playing_scene()
	return {"ok": true}


func tool_list_dir(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "res://")
	var dir := DirAccess.open(path)
	if dir == null:
		return {"error": "cannot open dir: %s" % path}
	var entries := []
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		if f != "." and f != "..":
			entries.append({"name": f, "is_dir": dir.current_is_dir()})
		f = dir.get_next()
	dir.list_dir_end()
	return {"ok": true, "entries": entries}


func tool_node_call_method(args: Dictionary) -> Dictionary:
	var node := _find_node(args.get("node_path", ""))
	if node == null:
		return {"error": "node not found"}
	var method: String = args.get("method", "")
	if method == "":
		return {"error": "missing method"}
	if not node.has_method(method):
		return {"error": "node has no method: %s" % method}
	var raw_args = args.get("args", [])
	if typeof(raw_args) != TYPE_ARRAY:
		raw_args = []
	var call_args: Array = []
	for a in raw_args:
		call_args.append(_coerce_value(null, a))
	var result = node.callv(method, call_args)
	return {"ok": true, "result": _json_safe(result)}


# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

func tool_screenshot(args: Dictionary) -> Dictionary:
	var path: String = args.get("path", "")
	if path == "":
		var ts := Time.get_datetime_string_from_system(false).replace(":", "")
		path = "user://editor_screenshot_%s.png" % ts
	# Try to grab the editor viewport texture
	var vp: Viewport = EditorInterface.get_editor_main_screen().get_viewport()
	if vp == null:
		return {"error": "no editor viewport found"}
	var tex: ViewportTexture = vp.get_texture()
	if tex == null:
		return {"error": "no viewport texture"}
	var img: Image = tex.get_image()
	if img == null:
		return {"error": "could not get image from viewport"}
	var err: int = img.save_png(path)
	if err != OK:
		return {"error": "save_png failed with error %d" % err}
	# Get absolute path for convenience
	var abs_path: String = ProjectSettings.globalize_path(path)
	_log("Screenshot saved to: %s" % abs_path)
	return {"ok": true, "path": abs_path}


# ---------------------------------------------------------------------------
# Query tools (compact alternatives to get_scene_tree)
# ---------------------------------------------------------------------------

func tool_node_find(args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	var name_pattern: String = args.get("name_pattern", "")
	var type_filter: String = args.get("type", "")
	var parent_prefix: String = args.get("parent_prefix", "")
	var max_results: int = int(args.get("limit", 100))
	var results: Array = []
	_find_recursive(root, root, name_pattern, type_filter, parent_prefix, max_results, results)
	return {"ok": true, "count": results.size(), "nodes": results}

func _find_recursive(node: Node, root: Node, name_pattern: String, type_filter: String, parent_prefix: String, max_results: int, results: Array) -> void:
	if results.size() >= max_results:
		return
	var rel_path: String = "." if node == root else str(root.get_path_to(node))
	# Check name match (simple wildcard)
	var name_ok: bool = name_pattern == "" or node.name.matchn(name_pattern)
	# Check type
	var type_ok: bool = type_filter == "" or node.is_class(type_filter)
	# Check parent prefix
	var prefix_ok: bool = parent_prefix == "" or rel_path.begins_with(parent_prefix)
	if name_ok and type_ok and prefix_ok:
		results.append({"name": node.name, "type": node.get_class(), "path": rel_path})
	if results.size() >= max_results:
		return
	for child in node.get_children():
		_find_recursive(child, root, name_pattern, type_filter, parent_prefix, max_results, results)
		if results.size() >= max_results:
			return

func tool_scene_stats(_args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	var by_type: Dictionary = {}
	var by_parent: Dictionary = {}
	var total: int = 0
	_count_recursive(root, root, by_type, by_parent, total)
	return {"ok": true, "total_nodes": total, "by_type": by_type, "by_parent": by_parent}

func _count_recursive(node: Node, root: Node, by_type: Dictionary, by_parent: Dictionary, total: int) -> void:
	total += 1
	var cls: String = node.get_class()
	by_type[cls] = int(by_type.get(cls, 0)) + 1
	if node == root:
		by_parent[node.name] = int(by_parent.get(node.name, 0)) + 1
	else:
		var top: String = str(root.get_path_to(node)).split("/")[0]
		by_parent[top] = int(by_parent.get(top, 0)) + 1
	for child in node.get_children():
		_count_recursive(child, root, by_type, by_parent, total)

func tool_material_report(args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	var max_missing: int = int(args.get("limit", 50))
	var with_mat: int = 0
	var without_mat: int = 0
	var missing_list: Array = []
	_check_materials_recursive(root, root, with_mat, without_mat, missing_list, max_missing)
	return {
		"ok": true,
		"with_material": with_mat,
		"without_material": without_mat,
		"coverage_pct": round(float(with_mat) / max(1.0, float(with_mat + without_mat)) * 100.0),
		"missing_count": without_mat,
		"missing_samples": missing_list,
	}

func _check_materials_recursive(node: Node, root: Node, with_mat: int, without_mat: int, missing_list: Array, max_missing: int) -> void:
	# Only check renderable types
	var cls: String = node.get_class()
	var is_renderable: bool = cls == "MeshInstance3D" or cls.begins_with("CSG")
	if is_renderable:
		var has_mat: bool = false
		if node.has_method("get") and node.get("material_override") != null:
			has_mat = true
		if not has_mat and node.has_method("get") and node.get("surface_material_override/0") != null:
			has_mat = true
		if has_mat:
			with_mat += 1
		else:
			without_mat += 1
			if missing_list.size() < max_missing:
				var rel_path: String = "." if node == root else str(root.get_path_to(node))
				missing_list.append({"name": node.name, "type": cls, "path": rel_path})
	for child in node.get_children():
		_check_materials_recursive(child, root, with_mat, without_mat, missing_list, max_missing)

func tool_screenshot_camera(args: Dictionary) -> Dictionary:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return {"error": "no scene open"}
	var pos = args.get("position", {})
	var look_at = args.get("look_at", {})
	var fov: float = float(args.get("fov", 60.0))
	if typeof(pos) != TYPE_DICTIONARY or typeof(look_at) != TYPE_DICTIONARY:
		return {"error": "position and look_at required as {x,y,z}"}
	
	# Create temporary camera
	var cam := Camera3D.new()
	cam.fov = fov
	cam.position = Vector3(float(pos.get("x", 0)), float(pos.get("y", 5)), float(pos.get("z", 0)))
	var target := Vector3(float(look_at.get("x", 0)), float(look_at.get("y", 0)), float(look_at.get("z", 0)))
	cam.look_at(target)
	root.add_child(cam)
	cam.owner = root
	cam.current = true
	
	# Wait a frame for viewport to update, then screenshot
	# Since we're in _process, we need to defer
	call_deferred("_do_screenshot_camera", cam)
	
	return {"ok": true, "message": "screenshot queued (check next screenshot)"}

func _do_screenshot_camera(cam: Camera3D) -> void:
	# Wait one frame then take screenshot
	await get_tree().process_frame
	await get_tree().process_frame
	
	var path: String = "user://editor_screenshot_%s.png" % Time.get_datetime_string_from_system(false).replace(":", "")
	var vp: Viewport = EditorInterface.get_editor_main_screen().get_viewport()
	if vp:
		var tex: ViewportTexture = vp.get_texture()
		if tex:
			var img: Image = tex.get_image()
			if img:
				img.save_png(path)
	
	# Cleanup camera
	cam.queue_free()
	_log("Free-camera screenshot saved to: %s" % ProjectSettings.globalize_path(path))


# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

func _log(msg: String) -> void:
	print("[AI Pipeline] ", msg)
	if dock and dock.has_method("append_log"):
		dock.append_log(msg)
