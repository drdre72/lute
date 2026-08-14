extends Node

## 3D tool library for the LLM agent.

signal tool_executed(tool_name: String, result: Dictionary)

var _tools: Dictionary = {}
var _world_root: Node = null
var _agent: Node3D = null
var _tts_voice: String = ""
var _inventory: Array = []
var _notes: Array[String] = []

func _safe_color(color_str: String) -> Color:
	var c = Color.from_string(color_str, Color.WHITE)
	return c

func setup(world_root: Node, agent: Node3D) -> void:
	_world_root = world_root
	_agent = agent
	_register_default_tools()
	_pick_female_voice()

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
	register_tool(
		"move_self",
		"Move the agent to a new 3D position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Target X position"},
				"y": {"type": "number", "description": "Target Y position (height)"},
				"z": {"type": "number", "description": "Target Z position"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_move_self")
	)

	register_tool(
		"spawn_box",
		"Spawn a 3D box (CSGBox3D) at a position. Good for building structures.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "World X"},
				"y": {"type": "number", "description": "World Y (height, default 0.5)"},
				"z": {"type": "number", "description": "World Z"},
				"w": {"type": "number", "description": "Width (default 1.0)"},
				"h": {"type": "number", "description": "Height (default 1.0)"},
				"d": {"type": "number", "description": "Depth (default 1.0)"},
				"color": {"type": "string", "description": "Color name or hex (e.g. 'red', '#ff0000')"},
				"name": {"type": "string", "description": "Optional node name"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_box")
	)

	register_tool(
		"spawn_sphere",
		"Spawn a 3D sphere (CSGSphere3D) at a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number", "description": "Height (default 0.5)"},
				"z": {"type": "number"},
				"r": {"type": "number", "description": "Radius (default 0.5)"},
				"color": {"type": "string"},
				"name": {"type": "string"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_sphere")
	)

	register_tool(
		"spawn_cylinder",
		"Spawn a 3D cylinder (CSGCylinder3D) at a position. Good for pillars.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number", "description": "Height (default 1.0)"},
				"z": {"type": "number"},
				"r": {"type": "number", "description": "Radius (default 0.5)"},
				"h": {"type": "number", "description": "Cylinder height (default 2.0)"},
				"color": {"type": "string"},
				"name": {"type": "string"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_cylinder")
	)

	register_tool(
		"spawn_light",
		"Spawn an OmniLight3D at a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number", "description": "Height (default 3.0)"},
				"z": {"type": "number"},
				"color": {"type": "string", "description": "Light color (default white)"},
				"energy": {"type": "number", "description": "Light energy (default 3.0)"},
				"range": {"type": "number", "description": "Light range (default 10.0)"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_light")
	)

	register_tool(
		"get_nearby_nodes",
		"Get a list of 3D nodes within a radius of a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number"},
				"z": {"type": "number"},
				"radius": {"type": "number", "description": "Search radius (default 20)"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_get_nearby_nodes")
	)

	register_tool(
		"get_world_state",
		"Get current world state: node count, children list, agent position.",
		{
			"type": "object",
			"properties": {},
			"required": []
		},
		Callable(self, "_tool_get_world_state")
	)

	register_tool(
		"delete_node",
		"Delete a node from the scene tree by path.",
		{
			"type": "object",
			"properties": {
				"node_path": {"type": "string", "description": "Path to the node"}
			},
			"required": ["node_path"]
		},
		Callable(self, "_tool_delete_node")
	)

	register_tool(
		"say",
		"Display a message from the agent.",
		{
			"type": "object",
			"properties": {
				"message": {"type": "string"}
			},
			"required": ["message"]
		},
		Callable(self, "_tool_say")
	)

	register_tool(
		"run_gdscript",
		"Execute raw GDScript code at runtime. Has access to 'world' (scene root) and 'agent' (the agent node).",
		{
			"type": "object",
			"properties": {
				"code": {"type": "string", "description": "GDScript code to execute."}
			},
			"required": ["code"]
		},
		Callable(self, "_tool_run_gdscript")
	)

	register_tool(
		"create_terrain",
		"Create a procedural terrain patch using a CSG box grid. Molds large areas of land like clay. Specify center, size, and height variation.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Center X"},
				"z": {"type": "number", "description": "Center Z"},
				"size": {"type": "number", "description": "Total size in meters (default 20)"},
				"resolution": {"type": "integer", "description": "Grid cells per side (default 8, max 20)"},
				"height": {"type": "number", "description": "Max height variation (default 2.0)"},
				"color": {"type": "string", "description": "Terrain color (default 'green')"},
				"name": {"type": "string", "description": "Optional name for the terrain group"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_create_terrain")
	)

	register_tool(
		"spawn_wall",
		"Spawn a wall segment between two points. Good for buildings and enclosures.",
		{
			"type": "object",
			"properties": {
				"x1": {"type": "number", "description": "Start X"},
				"z1": {"type": "number", "description": "Start Z"},
				"x2": {"type": "number", "description": "End X"},
				"z2": {"type": "number", "description": "End Z"},
				"height": {"type": "number", "description": "Wall height (default 3.0)"},
				"thickness": {"type": "number", "description": "Wall thickness (default 0.3)"},
				"color": {"type": "string", "description": "Wall color (default 'gray')"}
			},
			"required": ["x1", "z1", "x2", "z2"]
		},
		Callable(self, "_tool_spawn_wall")
	)

	register_tool(
		"spawn_tree",
		"Spawn a simple tree (cylinder trunk + sphere foliage) at a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"z": {"type": "number"},
				"scale": {"type": "number", "description": "Tree scale (default 1.0)"},
				"trunk_color": {"type": "string", "description": "Trunk color (default 'saddlebrown')"},
				"leaf_color": {"type": "string", "description": "Leaf color (default 'forestgreen')"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_tree")
	)

	register_tool(
		"spawn_water",
		"Spawn a flat translucent water plane at a position.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number", "description": "Water level height (default 0.1)"},
				"z": {"type": "number"},
				"size": {"type": "number", "description": "Water plane size (default 10)"},
				"color": {"type": "string", "description": "Water color (default 'deepskyblue')"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_water")
	)

	register_tool(
		"spawn_portal",
		"Spawn a Time Portal — a glowing torus with light. This is the spawn mechanism for the game.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number"},
				"y": {"type": "number", "description": "Height (default 1.0)"},
				"z": {"type": "number"},
				"color": {"type": "string", "description": "Portal glow color (default 'cyan')"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_portal")
	)

	register_tool(
		"checklist_update",
		"Mark a checklist item as complete. Use this to track your world-building progress.",
		{
			"type": "object",
			"properties": {
				"item": {"type": "string", "description": "The checklist item to mark complete"}
			},
			"required": ["item"]
		},
		Callable(self, "_tool_checklist_update")
	)

	register_tool(
		"save_world",
		"Save the current world (all spawned nodes) to a .tscn scene file so it persists. Call this after completing the checklist.",
		{
			"type": "object",
			"properties": {
				"filename": {"type": "string", "description": "Scene filename (default 'generated_world.tscn')"}
			},
			"required": []
		},
		Callable(self, "_tool_save_world")
	)

	register_tool(
		"teleport",
		"Instantly teleport the agent to a position (no walking animation).",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "X coordinate"},
				"y": {"type": "number", "description": "Y coordinate (default 0)"},
				"z": {"type": "number", "description": "Z coordinate"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_teleport")
	)

	register_tool(
		"scale_object",
		"Scale an existing spawned node by name. Multiply its current scale.",
		{
			"type": "object",
			"properties": {
				"name": {"type": "string", "description": "Node name to scale"},
				"scale": {"type": "number", "description": "Scale multiplier (e.g. 2.0 = double, 0.5 = half)"}
			},
			"required": ["name", "scale"]
		},
		Callable(self, "_tool_scale_object")
	)

	register_tool(
		"rotate_object",
		"Rotate an existing spawned node by name.",
		{
			"type": "object",
			"properties": {
				"name": {"type": "string", "description": "Node name to rotate"},
				"y_degrees": {"type": "number", "description": "Rotation around Y axis in degrees"}
			},
			"required": ["name", "y_degrees"]
		},
		Callable(self, "_tool_rotate_object")
	)

	register_tool(
		"recolor_object",
		"Change the color of an existing spawned node by name.",
		{
			"type": "object",
			"properties": {
				"name": {"type": "string", "description": "Node name to recolor"},
				"color": {"type": "string", "description": "New color name or hex (e.g. 'red', '#ff0000')"}
			},
			"required": ["name", "color"]
		},
		Callable(self, "_tool_recolor_object")
	)

	register_tool(
		"duplicate_object",
		"Duplicate an existing spawned node with a position offset.",
		{
			"type": "object",
			"properties": {
				"name": {"type": "string", "description": "Node name to duplicate"},
				"offset_x": {"type": "number", "description": "X offset for the copy (default 2)"},
				"offset_z": {"type": "number", "description": "Z offset for the copy (default 0)"}
			},
			"required": ["name"]
		},
		Callable(self, "_tool_duplicate_object")
	)

	register_tool(
		"spawn_stairs",
		"Spawn a staircase with N steps leading upward. Good for multi-level structures.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Base X position"},
				"z": {"type": "number", "description": "Base Z position"},
				"steps": {"type": "integer", "description": "Number of steps (default 8)"},
				"step_height": {"type": "number", "description": "Height per step (default 0.5)"},
				"step_width": {"type": "number", "description": "Width of each step (default 2)"},
				"color": {"type": "string", "description": "Step color (default 'gray')"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_stairs")
	)

	register_tool(
		"spawn_arch",
		"Spawn an archway using two pillars and a top beam. Good for gates and bridges.",
		{
			"type": "object",
			"properties": {
				"x": {"type": "number", "description": "Center X position"},
				"z": {"type": "number", "description": "Center Z position"},
				"width": {"type": "number", "description": "Span width between pillars (default 4)"},
				"height": {"type": "number", "description": "Pillar height (default 5)"},
				"color": {"type": "string", "description": "Arch color (default 'lightgray')"}
			},
			"required": ["x", "z"]
		},
		Callable(self, "_tool_spawn_arch")
	)

	register_tool(
		"inventory_add",
		"Add an item to your inventory. Use this when you collect or create something.",
		{
			"type": "object",
			"properties": {
				"item": {"type": "string", "description": "Name of the item to add"},
				"quantity": {"type": "integer", "description": "Quantity to add (default 1)"}
			},
			"required": ["item"]
		},
		Callable(self, "_tool_inventory_add")
	)

	register_tool(
		"inventory_remove",
		"Remove an item from your inventory. Use this when you use or consume something.",
		{
			"type": "object",
			"properties": {
				"item": {"type": "string", "description": "Name of the item to remove"},
				"quantity": {"type": "integer", "description": "Quantity to remove (default 1)"}
			},
			"required": ["item"]
		},
		Callable(self, "_tool_inventory_remove")
	)

	register_tool(
		"inventory_list",
		"List all items currently in your inventory.",
		{
			"type": "object",
			"properties": {},
			"required": []
		},
		Callable(self, "_tool_inventory_list")
	)

	register_tool(
		"add_note",
		"Write a note to yourself for later reference. Notes persist between actions.",
		{
			"type": "object",
			"properties": {
				"note": {"type": "string", "description": "The note text to remember"}
			},
			"required": ["note"]
		},
		Callable(self, "_tool_add_note")
	)

	register_tool(
		"read_notes",
		"Read all your saved notes. Call this to remind yourself of past observations and plans.",
		{
			"type": "object",
			"properties": {},
			"required": []
		},
		Callable(self, "_tool_read_notes")
	)

	register_tool(
		"load_build_plan",
		"Load build instructions from a file. Returns the full text of the build plan for you to follow.",
		{
			"type": "object",
			"properties": {
				"filename": {"type": "string", "description": "Path to the build plan file (e.g. res://instruct/build_rust_world.txt)"}
			},
			"required": ["filename"]
		},
		Callable(self, "_tool_load_build_plan")
	)

# --- Tool Handlers ---

func _tool_move_self(args: Dictionary) -> Dictionary:
	if _agent == null:
		return {"error": "No agent"}
	var x = float(args.get("x", 0))
	var y = float(args.get("y", _agent.position.y))
	var z = float(args.get("z", 0))
	var target = Vector3(x, y, z)
	_agent.set_meta("move_target", target)
	# Direct move for now
	_agent.position = target
	return {"ok": true, "position": {"x": x, "y": y, "z": z}}

func _tool_spawn_box(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var box = CSGBox3D.new()
	box.position = Vector3(float(args.get("x", 0)), float(args.get("y", 0.5)), float(args.get("z", 0)))
	box.size = Vector3(float(args.get("w", 1.0)), float(args.get("h", 1.0)), float(args.get("d", 1.0)))
	var color_str: String = args.get("color", "white")
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	box.material = mat
	var node_name: String = args.get("name", "")
	if node_name != "":
		box.name = node_name
	_world_root.add_child(box)
	box.owner = _world_root
	return {"ok": true, "node": box.name, "pos": {"x": box.position.x, "y": box.position.y, "z": box.position.z}}

func _tool_spawn_sphere(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var sphere = CSGSphere3D.new()
	sphere.position = Vector3(float(args.get("x", 0)), float(args.get("y", 0.5)), float(args.get("z", 0)))
	sphere.radius = float(args.get("r", 0.5))
	var color_str: String = args.get("color", "white")
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	sphere.material = mat
	var node_name: String = args.get("name", "")
	if node_name != "":
		sphere.name = node_name
	_world_root.add_child(sphere)
	sphere.owner = _world_root
	return {"ok": true, "node": sphere.name}

func _tool_spawn_cylinder(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var cyl = CSGCylinder3D.new()
	cyl.position = Vector3(float(args.get("x", 0)), float(args.get("y", 1.0)), float(args.get("z", 0)))
	cyl.radius = float(args.get("r", 0.5))
	cyl.height = float(args.get("h", 2.0))
	var color_str: String = args.get("color", "gray")
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	cyl.material = mat
	var node_name: String = args.get("name", "")
	if node_name != "":
		cyl.name = node_name
	_world_root.add_child(cyl)
	cyl.owner = _world_root
	return {"ok": true, "node": cyl.name}

func _tool_spawn_light(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var light = OmniLight3D.new()
	light.position = Vector3(float(args.get("x", 0)), float(args.get("y", 3.0)), float(args.get("z", 0)))
	var color_str: String = args.get("color", "white")
	light.light_color = _safe_color(color_str)
	light.light_energy = float(args.get("energy", 3.0))
	light.omni_range = float(args.get("range", 10.0))
	_world_root.add_child(light)
	light.owner = _world_root
	return {"ok": true, "node": light.name}

func _tool_get_nearby_nodes(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var center = Vector3(float(args.get("x", 0)), float(args.get("y", 0)), float(args.get("z", 0)))
	var radius = float(args.get("radius", 20.0))
	var results: Array = []
	for child in _world_root.get_children():
		if child == _agent:
			continue
		if child is Node3D:
			var dist = child.position.distance_to(center)
			if dist <= radius:
				results.append({
					"name": child.name,
					"type": child.get_class(),
					"x": child.position.x,
					"y": child.position.y,
					"z": child.position.z,
					"distance": dist
				})
	return {"ok": true, "nodes": results, "count": results.size()}

func _tool_get_world_state(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var children_info: Array = []
	for child in _world_root.get_children():
		var info = {"name": child.name, "type": child.get_class()}
		if child is Node3D:
			info["x"] = child.position.x
			info["y"] = child.position.y
			info["z"] = child.position.z
		children_info.append(info)
	return {
		"ok": true,
		"node_count": _world_root.get_child_count(),
		"children": children_info,
		"agent_position": {"x": _agent.position.x, "y": _agent.position.y, "z": _agent.position.z}
	}

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

func _tool_say(args: Dictionary) -> Dictionary:
	var message: String = args.get("message", "")
	print("[LLM Agent3D]: %s" % message)
	
	# Create 3D speech bubble above agent
	if _agent and is_instance_valid(_agent):
		_show_speech_bubble(_agent, message)
	
	# Speak via TTS
	if message.strip_edges() != "" and _tts_voice != "":
		DisplayServer.tts_speak(message, _tts_voice, 40, 1.4, 0.9, false)
	
	return {"ok": true, "message": message}

func _tool_inventory_add(args: Dictionary) -> Dictionary:
	var item: String = args.get("item", "")
	var qty: int = int(args.get("quantity", 1))
	if item == "":
		return {"error": "No item specified"}
	# Check if item already exists
	for entry in _inventory:
		if entry.item == item:
			entry.quantity += qty
			print("[LLMTools] Inventory: %s x%d (total %d)" % [item, qty, entry.quantity])
			return {"ok": true, "item": item, "quantity": entry.quantity}
	_inventory.append({"item": item, "quantity": qty})
	print("[LLMTools] Inventory added: %s x%d" % [item, qty])
	return {"ok": true, "item": item, "quantity": qty}

func _tool_inventory_remove(args: Dictionary) -> Dictionary:
	var item: String = args.get("item", "")
	var qty: int = int(args.get("quantity", 1))
	if item == "":
		return {"error": "No item specified"}
	for i in range(_inventory.size() - 1, -1, -1):
		if _inventory[i].item == item:
			_inventory[i].quantity -= qty
			if _inventory[i].quantity <= 0:
				_inventory.remove_at(i)
				print("[LLMTools] Inventory removed: %s (all)" % item)
				return {"ok": true, "item": item, "quantity": 0}
			print("[LLMTools] Inventory removed: %s x%d (remaining %d)" % [item, qty, _inventory[i].quantity])
			return {"ok": true, "item": item, "quantity": _inventory[i].quantity}
	return {"error": "Item not in inventory: %s" % item}

func _tool_inventory_list(args: Dictionary) -> Dictionary:
	if _inventory.is_empty():
		return {"ok": true, "inventory": [], "message": "Inventory is empty"}
	var items: Array = []
	for entry in _inventory:
		items.append("%s x%d" % [entry.item, entry.quantity])
	var summary = ", ".join(items)
	print("[LLMTools] Inventory: %s" % summary)
	return {"ok": true, "inventory": _inventory.duplicate(true), "summary": summary}

func _tool_add_note(args: Dictionary) -> Dictionary:
	var note: String = args.get("note", "")
	if note == "":
		return {"error": "No note text specified"}
	_notes.append(note)
	print("[LLMTools] Note added: %s" % note)
	return {"ok": true, "note_count": _notes.size()}

func _tool_read_notes(args: Dictionary) -> Dictionary:
	if _notes.is_empty():
		return {"ok": true, "notes": [], "message": "No notes saved"}
	print("[LLMTools] Reading %d notes:" % _notes.size())
	for i in range(_notes.size()):
		print("[LLMTools]   Note %d: %s" % [i + 1, _notes[i]])
	return {"ok": true, "notes": _notes.duplicate()}

func _tool_load_build_plan(args: Dictionary) -> Dictionary:
	var filename: String = args.get("filename", "")
	if filename == "":
		return {"error": "No filename specified"}
	var f = FileAccess.open(filename, FileAccess.READ)
	if f == null:
		return {"error": "Cannot open file: %s" % filename}
	var content = f.get_as_text()
	f.close()
	print("[LLMTools] Loaded build plan from %s (%d chars)" % [filename, content.length()])
	return {"ok": true, "plan": content, "length": content.length()}

func _pick_female_voice() -> void:
	var voices = DisplayServer.tts_get_voices()
	if voices.is_empty():
		print("[LLMTools] No TTS voices available")
		return
	# Priority list: prefer enhanced/softer female voices
	var priority = ["samantha", "fiona", "karen", "tessa", "moira", "fiona (enhanced)", "samantha (enhanced)"]
	for pref in priority:
		for v in voices:
			var name = v.get("name", "")
			if name.to_lower() == pref or name.to_lower().contains(pref):
				_tts_voice = name
				print("[LLMTools] Selected voice: %s" % name)
				return
	# Fallback: any voice with female indicator
	for v in voices:
		var name = v.get("name", "")
		var lower = name.to_lower()
		if "female" in lower or "samantha" in lower or "victoria" in lower or "karen" in lower or "fiona" in lower or "tessa" in lower or "moira" in lower or "zira" in lower:
			_tts_voice = name
			print("[LLMTools] Selected female voice: %s" % name)
			return
	# Last resort: first voice
	_tts_voice = voices[0].get("name", "")
	print("[LLMTools] Using fallback voice: %s" % _tts_voice)

func _show_speech_bubble(agent: Node3D, text: String) -> void:
	# Remove any existing bubble
	var existing = agent.get_node_or_null("SpeechBubble")
	if existing:
		existing.queue_free()
	var existing_bg = agent.get_node_or_null("BubbleBG")
	if existing_bg:
		existing_bg.queue_free()
	
	if text.length() > 100:
		text = text.substr(0, 97) + "..."
	
	# Background plate - wider and taller for readability
	var bg = MeshInstance3D.new()
	bg.name = "BubbleBG"
	var plane = PlaneMesh.new()
	var bubble_width = max(text.length() * 0.5, 3.0)
	var bubble_height = 1.5
	plane.size = Vector2(bubble_width, bubble_height)
	bg.mesh = plane
	var bg_mat = StandardMaterial3D.new()
	bg_mat.albedo_color = Color(0.1, 0.1, 0.15, 0.85)
	bg_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	bg_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	bg_mat.no_depth_test = true
	bg_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	bg_mat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	bg.material_override = bg_mat
	bg.position = Vector3(0, 3.5, 0)
	
	# Text label - smaller font, proper sizing
	var bubble = Label3D.new()
	bubble.name = "SpeechBubble"
	bubble.text = text
	bubble.font_size = 24
	bubble.outline_modulate = Color(0, 0, 0, 0.8)
	bubble.outline_size = 8
	bubble.position = Vector3(0, 3.5, 0.02)
	bubble.no_depth_test = true
	bubble.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	bubble.width = int(bubble_width * 100)
	bubble.modulate = Color.WHITE
	
	# Text material with billboard
	var text_mat = StandardMaterial3D.new()
	text_mat.albedo_color = Color.WHITE
	text_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	text_mat.no_depth_test = true
	text_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	text_mat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	bubble.material_override = text_mat
	
	agent.add_child(bg)
	bg.owner = null
	agent.add_child(bubble)
	bubble.owner = null
	
	# Auto-remove after 5 seconds - use node-based timer to avoid lambda capture issues
	var timer_node = Timer.new()
	timer_node.name = "BubbleTimer"
	timer_node.wait_time = 5.0
	timer_node.one_shot = true
	agent.add_child(timer_node)
	timer_node.owner = null
	timer_node.timeout.connect(_remove_speech_bubble.bind(agent))
	timer_node.start()

func _remove_speech_bubble(agent: Node3D) -> void:
	if not is_instance_valid(agent):
		return
	var bubble = agent.get_node_or_null("SpeechBubble")
	if bubble:
		bubble.queue_free()
	var bg = agent.get_node_or_null("BubbleBG")
	if bg:
		bg.queue_free()
	var timer = agent.get_node_or_null("BubbleTimer")
	if timer:
		timer.queue_free()

func _tool_run_gdscript(args: Dictionary) -> Dictionary:
	var code: String = args.get("code", "")
	if code == "":
		return {"error": "No code provided"}
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
	var result = runner.call("_run", _world_root, _agent)
	runner.queue_free()
	return {"ok": true, "result": str(result)}

func _tool_create_terrain(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var cx = float(args.get("x", 0))
	var cz = float(args.get("z", 0))
	var size = float(args.get("size", 20))
	var res = int(args.get("resolution", 16))
	res = clamp(res, 4, 32)
	var max_h = float(args.get("height", 2.0))
	var color_str: String = args.get("color", "darkgreen")
	var group_name: String = args.get("name", "Terrain")
	
	# Build a smooth terrain mesh using ArrayMesh
	var group = Node3D.new()
	group.name = group_name
	group.position = Vector3(cx, 0, cz)
	_world_root.add_child(group)
	group.owner = _world_root
	
	var mesh_inst = MeshInstance3D.new()
	mesh_inst.name = "TerrainMesh"
	
	var arr_mesh = ArrayMesh.new()
	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	
	var verts: PackedVector3Array = PackedVector3Array()
	var uvs: PackedVector2Array = PackedVector2Array()
	var indices: PackedInt32Array = PackedInt32Array()
	
	# Generate vertices with smooth heightmap
	for iz in range(res + 1):
		for ix in range(res + 1):
			var fx = float(ix) / res - 0.5
			var fz = float(iz) / res - 0.5
			var wx = fx * size
			var wz = fz * size
			# Multi-octave noise for natural terrain
			var h = 0.0
			h += max_h * sin(wx * 0.15) * cos(wz * 0.15)
			h += max_h * 0.5 * sin(wx * 0.35 + 1.0) * cos(wz * 0.3 + 0.5)
			h += max_h * 0.25 * sin(wx * 0.7 + 2.0) * cos(wz * 0.6 + 1.5)
			# Flatten edges slightly for blending
			var edge_fade = 1.0 - pow(max(abs(fx), abs(fz)) * 2.0, 3.0)
			edge_fade = clamp(edge_fade, 0.3, 1.0)
			h *= edge_fade
			verts.append(Vector3(wx, h, wz))
			uvs.append(Vector2(fx + 0.5, fz + 0.5))
	
	# Generate indices
	for iz in range(res):
		for ix in range(res):
			var i = iz * (res + 1) + ix
			indices.append(i)
			indices.append(i + res + 1)
			indices.append(i + 1)
			indices.append(i + 1)
			indices.append(i + res + 1)
			indices.append(i + res + 2)
	
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	arr_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	
	mesh_inst.mesh = arr_mesh
	
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	mat.roughness = 0.9
	mesh_inst.material_override = mat
	
	group.add_child(mesh_inst)
	mesh_inst.owner = _world_root
	
	return {"ok": true, "vertices": verts.size(), "size": size, "resolution": res, "group": group.name}

func _tool_spawn_wall(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x1 = float(args.get("x1", 0))
	var z1 = float(args.get("z1", 0))
	var x2 = float(args.get("x2", 0))
	var z2 = float(args.get("z2", 0))
	var h = float(args.get("height", 3.0))
	var thick = float(args.get("thickness", 0.3))
	var color_str: String = args.get("color", "gray")
	
	var mid_x = (x1 + x2) / 2.0
	var mid_z = (z1 + z2) / 2.0
	var length = sqrt(pow(x2 - x1, 2) + pow(z2 - z1, 2))
	var angle = atan2(z2 - z1, x2 - x1)
	
	var wall = CSGBox3D.new()
	wall.position = Vector3(mid_x, h * 0.5, mid_z)
	wall.size = Vector3(length, h, thick)
	wall.rotation.y = -angle
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	wall.material = mat
	_world_root.add_child(wall)
	wall.owner = _world_root
	return {"ok": true, "length": length, "pos": {"x": mid_x, "z": mid_z}}

func _tool_spawn_tree(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x = float(args.get("x", 0))
	var z = float(args.get("z", 0))
	var s = float(args.get("scale", 1.0))
	var trunk_color: String = args.get("trunk_color", "saddlebrown")
	var leaf_color: String = args.get("leaf_color", "forestgreen")
	
	var group = Node3D.new()
	group.name = "Tree_%d_%d" % [int(x), int(z)]
	group.position = Vector3(x, 0, z)
	group.scale = Vector3(s, s, s)
	_world_root.add_child(group)
	group.owner = _world_root
	
	var trunk = CSGCylinder3D.new()
	trunk.position = Vector3(0, 1.0, 0)
	trunk.radius = 0.2
	trunk.height = 2.0
	var tmat = StandardMaterial3D.new()
	tmat.albedo_color = _safe_color(trunk_color)
	trunk.material = tmat
	group.add_child(trunk)
	trunk.owner = _world_root
	
	var leaves = CSGSphere3D.new()
	leaves.position = Vector3(0, 2.5, 0)
	leaves.radius = 1.0
	var lmat = StandardMaterial3D.new()
	lmat.albedo_color = _safe_color(leaf_color)
	leaves.material = lmat
	group.add_child(leaves)
	leaves.owner = _world_root
	
	return {"ok": true, "node": group.name}

func _tool_spawn_water(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x = float(args.get("x", 0))
	var y = float(args.get("y", 0.1))
	var z = float(args.get("z", 0))
	var sz = float(args.get("size", 10))
	var color_str: String = args.get("color", "deepskyblue")
	
	var water = CSGBox3D.new()
	water.position = Vector3(x, y, z)
	water.size = Vector3(sz, 0.1, sz)
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color.a = 0.6
	water.material = mat
	_world_root.add_child(water)
	water.owner = _world_root
	return {"ok": true, "node": water.name}

func _tool_spawn_portal(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x = float(args.get("x", 0))
	var y = float(args.get("y", 1.0))
	var z = float(args.get("z", 0))
	var color_str: String = args.get("color", "cyan")
	
	var group = Node3D.new()
	group.name = "TimePortal"
	group.position = Vector3(x, 0, z)
	_world_root.add_child(group)
	group.owner = _world_root
	
	# Torus ring
	var ring = CSGTorus3D.new()
	ring.position = Vector3(0, y, 0)
	ring.outer_radius = 1.5
	ring.inner_radius = 0.2
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	mat.emission_energy_multiplier = 2.0
	mat.emission = _safe_color(color_str)
	ring.material = mat
	group.add_child(ring)
	ring.owner = _world_root
	
	# Glow light
	var light = OmniLight3D.new()
	light.position = Vector3(0, y, 0)
	light.light_color = _safe_color(color_str)
	light.light_energy = 5.0
	light.omni_range = 8.0
	group.add_child(light)
	light.owner = _world_root
	
	return {"ok": true, "node": group.name, "pos": {"x": x, "y": y, "z": z}}

func _tool_checklist_update(args: Dictionary) -> Dictionary:
	var item: String = args.get("item", "")
	if item == "":
		return {"error": "No item specified"}
	if _agent and _agent.has_method("_add_log"):
		_agent._add_log("[color=#44ff44][CHECKLIST DONE]: %s[/color]" % item)
	return {"ok": true, "item": item}

func _tool_save_world(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var fname: String = args.get("filename", "generated_world.tscn")
	var path = "user://" + fname
	
	var packed = PackedScene.new()
	var err = packed.pack(_world_root)
	if err != OK:
		return {"error": "Failed to pack scene: %d" % err}
	
	err = ResourceSaver.save(packed, path)
	if err != OK:
		return {"error": "Failed to save: %d" % err}
	
	if _agent and _agent.has_method("_add_log"):
		_agent._add_log("[color=#44ff44][WORLD SAVED]: %s[/color]" % path)
	
	return {"ok": true, "path": path, "children": _world_root.get_child_count()}

func _find_node_by_name(name: String) -> Node:
	if _world_root == null:
		return null
	for child in _world_root.get_children():
		if child.name == name:
			return child
		# Check one level deep
		for sub in child.get_children():
			if sub.name == name:
				return sub
	return null

func _tool_teleport(args: Dictionary) -> Dictionary:
	if _agent == null:
		return {"error": "No agent"}
	var x = float(args.get("x", 0))
	var y = float(args.get("y", _agent.position.y))
	var z = float(args.get("z", 0))
	_agent.position = Vector3(x, y, z)
	_agent.set_meta("is_moving", false)
	return {"ok": true, "pos": {"x": x, "y": y, "z": z}}

func _tool_scale_object(args: Dictionary) -> Dictionary:
	var node_name: String = args.get("name", "")
	var s = float(args.get("scale", 1.0))
	var node = _find_node_by_name(node_name)
	if node == null:
		return {"error": "Node not found: %s" % node_name}
	node.scale *= s
	return {"ok": true, "name": node_name, "new_scale": node.scale}

func _tool_rotate_object(args: Dictionary) -> Dictionary:
	var node_name: String = args.get("name", "")
	var deg = float(args.get("y_degrees", 0))
	var node = _find_node_by_name(node_name)
	if node == null:
		return {"error": "Node not found: %s" % node_name}
	node.rotation.y = deg_to_rad(deg)
	return {"ok": true, "name": node_name, "rotation_y": deg}

func _tool_recolor_object(args: Dictionary) -> Dictionary:
	var node_name: String = args.get("name", "")
	var color_str: String = args.get("color", "white")
	var node = _find_node_by_name(node_name)
	if node == null:
		return {"error": "Node not found: %s" % node_name}
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	if node is CSGShape3D:
		node.material = mat
	elif node is MeshInstance3D:
		node.material_override = mat
	else:
		# Try to find a mesh child
		for child in node.find_children("*", "MeshInstance3D", true, false):
			child.material_override = mat
		for child in node.find_children("*", "CSGShape3D", true, false):
			child.material = mat
	return {"ok": true, "name": node_name, "color": color_str}

func _tool_duplicate_object(args: Dictionary) -> Dictionary:
	var node_name: String = args.get("name", "")
	var ox = float(args.get("offset_x", 2.0))
	var oz = float(args.get("offset_z", 0.0))
	var node = _find_node_by_name(node_name)
	if node == null:
		return {"error": "Node not found: %s" % node_name}
	var dup = node.duplicate()
	dup.position += Vector3(ox, 0, oz)
	dup.name = node_name + "_copy"
	_world_root.add_child(dup)
	dup.owner = _world_root
	return {"ok": true, "name": dup.name, "pos": {"x": dup.position.x, "z": dup.position.z}}

func _tool_spawn_stairs(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x = float(args.get("x", 0))
	var z = float(args.get("z", 0))
	var steps = int(args.get("steps", 8))
	var sh = float(args.get("step_height", 0.5))
	var sw = float(args.get("step_width", 2.0))
	var color_str: String = args.get("color", "gray")
	
	var group = Node3D.new()
	group.name = "Stairs_%d_%d" % [int(x), int(z)]
	group.position = Vector3(x, 0, z)
	_world_root.add_child(group)
	group.owner = _world_root
	
	for i in range(steps):
		var step = CSGBox3D.new()
		step.name = "Step_%d" % i
		step.size = Vector3(sw, sh, 1.0)
		step.position = Vector3(0, sh * 0.5 + sh * i, -i * 1.0)
		var mat = StandardMaterial3D.new()
		mat.albedo_color = _safe_color(color_str)
		step.material = mat
		group.add_child(step)
		step.owner = _world_root
	
	return {"ok": true, "node": group.name, "steps": steps}

func _tool_spawn_arch(args: Dictionary) -> Dictionary:
	if _world_root == null:
		return {"error": "No world root"}
	var x = float(args.get("x", 0))
	var z = float(args.get("z", 0))
	var w = float(args.get("width", 4.0))
	var h = float(args.get("height", 5.0))
	var color_str: String = args.get("color", "lightgray")
	
	var group = Node3D.new()
	group.name = "Arch_%d_%d" % [int(x), int(z)]
	group.position = Vector3(x, 0, z)
	_world_root.add_child(group)
	group.owner = _world_root
	
	var mat = StandardMaterial3D.new()
	mat.albedo_color = _safe_color(color_str)
	
	# Left pillar
	var left = CSGBox3D.new()
	left.name = "PillarLeft"
	left.size = Vector3(0.6, h, 0.6)
	left.position = Vector3(-w * 0.5, h * 0.5, 0)
	left.material = mat
	group.add_child(left)
	left.owner = _world_root
	
	# Right pillar
	var right = CSGBox3D.new()
	right.name = "PillarRight"
	right.size = Vector3(0.6, h, 0.6)
	right.position = Vector3(w * 0.5, h * 0.5, 0)
	right.material = mat
	group.add_child(right)
	right.owner = _world_root
	
	# Top beam
	var beam = CSGBox3D.new()
	beam.name = "Beam"
	beam.size = Vector3(w + 0.6, 0.6, 0.6)
	beam.position = Vector3(0, h, 0)
	beam.material = mat
	group.add_child(beam)
	beam.owner = _world_root
	
	return {"ok": true, "node": group.name, "width": w, "height": h}
