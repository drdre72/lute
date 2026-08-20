extends Node
## Registry of tools the LLM agent can call.
## Each tool is a Dictionary with: name, description, parameters, handler (Callable)

signal tool_executed(tool_name: String, result: Dictionary)

var _tools: Dictionary = {}
var _world_root: Node = null

func setup(world_root: Node) -> void:
	_world_root = world_root
	_register_default_tools()

func get_tool_definitions() -> Array:
	var defs: Array = []
	for name in _tools:
		var t = _tools[name]
		defs.append({
			"type": "function",
			"function": {
				"name": name,
				"description": t.description,
				"parameters": t.parameters
			}
		})
	return defs

func execute_tool(tool_name: String, args: Dictionary) -> Dictionary:
	if not _tools.has(tool_name):
		return {"error": "Unknown tool: %s" % tool_name}
	var t = _tools[tool_name]
	var handler: Callable = t.handler
	var result = handler.call(args)
	tool_executed.emit(tool_name, result)
	return result

func register_tool(name: String, description: String, parameters: Dictionary, handler: Callable) -> void:
	_tools[name] = {
		"description": description,
		"parameters": parameters,
		"handler": handler
	}

func _register_default_tools() -> void:
	# --- Movement ---
	register_tool(
		"move_self",
		"Move the agent sprite to a new position in the world.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Target X position"},
				"y": {"type": "number", "description": "Target Y position"},
				"speed": {"type": "number", "description": "Movement speed multiplier (default 1.0)"}
			},
			"required": ["x", "y"]
		},
		Callable(self, "_tool_move_self")
	)

	# --- Spawning ---
	register_tool(
		"spawn_node",
		"Spawn a scene instance at a position. Use resource paths like res://scenes/ or built-in types.",
		{
			"type": "object",
			"properties": {
				"type": {"type": "string", "description": "Node type to spawn (e.g. 'Sprite2D', 'CharacterBody2D', 'RigidBody2D', 'Area2D')"},
				"x": {"type": "number", "description": "World X position"},
				"y": {"type": "number", "description": "World Y position"},
				"name": {"type": "string", "description": "Optional name for the node"}
			},
			"required": ["type", "x", "y"]
		},
		Callable(self, "_tool_spawn_node")
	)

	register_tool(
		"spawn_scene",
		"Instantiate a packed scene from a resource path and place it in the world.",
		{
			"type": "object",
			"properties": {
				"path": {"type": "string", "description": "Resource path like res://scenes/enemy.tscn"},
				"x": {"type": "number", "description": "World X position"},
				"y": {"type": "number", "description": "World Y position"}
			},
			"required": ["path", "x", "y"]
		},
		Callable(self, "_tool_spawn_scene")
	)

	# --- World inspection ---
	register_tool(
		"get_nearby_nodes",
		"Get a list of nodes within a radius of a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Center X"},
				"y": {"type": "number", "description": "Center Y"},
				"radius": {"type": "number", "description": "Search radius (default 200)"}
			},
			"required": ["x", "y"]
		},
		Callable(self, "_tool_get_nearby_nodes")
	)

	register_tool(
		"get_world_state",
		"Get the current world state: node count, scene tree structure, and agent position.",
		{
			"type": "object",
			"properties": {},
			"required": []
		},
		Callable(self, "_tool_get_world_state")
	)

	# --- Modification ---
	register_tool(
		"set_property",
		"Set a property on a node by path.",
		{
			"type": "object",
			"properties": {
				"node_path": {"type": "string", "description": "Path to the node (e.g. 'Player' or 'Enemies/Enemy1')"},
				"property": {"type": "string", "description": "Property name to set"},
				"value": {"type": "string", "description": "Value as string (will be coerced)"}
			},
			"required": ["node_path", "property", "value"]
		},
		Callable(self, "_tool_set_property")
	)

	register_tool(
		"delete_node",
		"Delete a node from the scene tree by path.",
		{
			"type": "object",
			"properties": {
				"node_path": {"type": "string", "description": "Path to the node to delete"}
			},
			"required": ["node_path"]
		},
		Callable(self, "_tool_delete_node")
	)

	register_tool(
		"create_color_rect",
		"Create a colored rectangle (simulates placing tiles/blocks). Good for building structures.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "World X position"},
				"y": {"type": "number", "description": "World Y position"},
				"w": {"type": "number", "description": "Width in pixels (default 32)"},
				"h": {"type": "number", "description": "Height in pixels (default 32)"},
				"color": {"type": "string", "description": "Color name or hex (e.g. 'red', '#ff0000', 'blue')"},
				"name": {"type": "string", "description": "Optional node name"}
			},
			"required": ["x", "y"]
		},
		Callable(self, "_tool_create_color_rect")
	)

	register_tool(
		"say",
		"Display a speech bubble or print a message from the agent.",
		{
			"type": "object",
			"properties": {
				"message": {"type": "string", "description": "The message to say"}
			},
			"required": ["message"]
		},
		Callable(self, "_tool_say")
	)

	register_tool(
		"run_gdscript",
		"Execute raw GDScript code at runtime. The code runs with access to 'world' (the scene root) and 'self' (the agent). Use with caution.",
		{
			"type": "object",
			"properties": {
				"code": {"type": "string", "description": "GDScript code to execute. Has access to 'world' (scene root) and 'agent' (the agent node)."}
			},
			"required": ["code"]
		},
		Callable(self, "_tool_run_gdscript")
	)

# --- Tool Handlers ---

func _tool_move_self(args: Dictionary) -> Dictionary:
	var agent = get_parent()
	if agent == null:
		return {"error": "No agent parent"}
	var target = Vector2(float(args.get("x", 0)), float(args.get("y", 0)))
	var speed = float(args.get("speed", 1.0))
	# Set a target for the agent to move toward
	agent.set_meta("move_target", target)
	agent.set_meta("move_speed", speed)
	return {"ok": true, "target": {"x": target.x, "y": target.y}}

func _tool_spawn_node(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var type_name: String = args.get("type", "")
	if type_name == "":
		return {"error": "Missing type"}
	if not ClassDB.class_exists(type_name):
		return {"error": "Unknown type: %s" % type_name}
	var node = ClassDB.instantiate(type_name)
	if node is Node2D:
		node.position = Vector2(float(args.get("x", 0)), float(args.get("y", 0)))
	var node_name: String = args.get("name", "")
	if node_name != "":
		node.name = node_name
	_world_root.add_child(node)
	node.owner = _world_root
	return {"ok": true, "node": node.name, "type": type_name}

func _tool_spawn_scene(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var path: String = args.get("path", "")
	if path == "":
		return {"error": "Missing path"}
	var res = load(path)
	if res == null:
		return {"error": "Failed to load: %s" % path}
	var instance = res.instantiate()
	if instance is Node2D:
		instance.position = Vector2(float(args.get("x", 0)), float(args.get("y", 0)))
	_world_root.add_child(instance)
	instance.owner = _world_root
	return {"ok": true, "node": instance.name}

func _tool_get_nearby_nodes(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var center = Vector2(float(args.get("x", 0)), float(args.get("y", 0)))
	var radius = float(args.get("radius", 200.0))
	var results: Array = []
	for child in _world_root.get_children():
		if child is Node2D:
			var dist = child.position.distance_to(center)
			if dist <= radius:
				results.append({
					"name": child.name,
					"type": child.get_class(),
					"x": child.position.x,
					"y": child.position.y,
					"distance": dist
				})
	return {"ok": true, "nodes": results, "count": results.size()}

func _tool_get_world_state(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var agent = get_parent()
	var children_info: Array = []
	for child in _world_root.get_children():
		var info = {"name": child.name, "type": child.get_class()}
		if child is Node2D:
			info["x"] = child.position.x
			info["y"] = child.position.y
		children_info.append(info)
	return {
		"ok": true,
		"node_count": _world_root.get_child_count(),
		"children": children_info,
		"agent_position": {"x": agent.position.x, "y": agent.position.y} if agent is Node2D else null
	}

func _tool_set_property(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var node_path: String = args.get("node_path", "")
	var prop: String = args.get("property", "")
	var val: String = args.get("value", "")
	var node = _world_root.get_node_or_null(NodePath(node_path))
	if node == null:
		return {"error": "Node not found: %s" % node_path}
	var current = node.get(prop)
	match typeof(current):
		TYPE_INT:
			node.set(prop, int(val))
		TYPE_FLOAT:
			node.set(prop, float(val))
		TYPE_BOOL:
			node.set(prop, val.to_lower() == "true")
		TYPE_VECTOR2:
			var parts = val.split(",")
			if parts.size() >= 2:
				node.set(prop, Vector2(float(parts[0]), float(parts[1])))
		TYPE_COLOR:
			node.set(prop, Color(val))
		_:
			node.set(prop, val)
	return {"ok": true, "node": node_path, "property": prop, "value": val}

func _tool_delete_node(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var node_path: String = args.get("node_path", "")
	var node = _world_root.get_node_or_null(NodePath(node_path))
	if node == null:
		return {"error": "Node not found: %s" % node_path}
	var name = node.name
	node.queue_free()
	return {"ok": true, "deleted": name}

func _tool_create_color_rect(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var rect = ColorRect.new()
	rect.position = Vector2(float(args.get("x", 0)), float(args.get("y", 0)))
	rect.size = Vector2(float(args.get("w", 32)), float(args.get("h", 32)))
	var color_str: String = args.get("color", "white")
	rect.color = Color(color_str)
	var node_name: String = args.get("name", "")
	if node_name != "":
		rect.name = node_name
	_world_root.add_child(rect)
	rect.owner = _world_root
	return {"ok": true, "node": rect.name, "position": {"x": rect.position.x, "y": rect.position.y}}

func _tool_say(args: Dictionary) -> Dictionary:
	var message: String = args.get("message", "")
	print("[LLM Agent]: %s" % message)
	var agent = get_parent()
	if agent:
		agent.set_meta("speech", message)
		agent.set_meta("speech_time", Time.get_ticks_msec())
	return {"ok": true, "message": message}

func _tool_run_gdscript(args: Dictionary) -> Dictionary:
	var code: String = args.get("code", "")
	if code == "":
		return {"error": "No code provided"}
	var agent = get_parent()
	var full_code = "extends Node\n\nfunc _run(world, agent):\n"
	for line in code.split("\n"):
		full_code += "\t" + line + "\n"
	var script = GDScript.new()
	script.source_code = full_code
	var err = script.reload()
	if err != OK:
		return {"error": "Script compile error: %d" % err}
	var runner = Node.new()
	runner.set_script(script)
	_world_root.add_child(runner)
	var result = runner.call("_run", _world_root, agent)
	runner.queue_free()
	return {"ok": true, "result": str(result)}
