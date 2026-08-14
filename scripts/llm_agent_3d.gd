extends Node3D

const LLMTools = preload("res://scripts/llm_tools_3d.gd")

## An LLM-controlled 3D agent that lives inside the Godot scene tree.
## Sends world context to a local LLM and executes structured tool calls.

signal llm_response_received(response: String)
signal tool_call_executed(tool_name: String, result: Dictionary)

@export_category("LLM Configuration")
@export var api_url: String = "https://api.x.ai/v1/chat/completions"
@export var model_name: String = "grok-beta"
@export var api_key: String = ""
@export var system_prompt: String = ""
@export var max_tokens: int = 1024
@export var temperature: float = 0.7

@export_category("Agent Behavior")
@export var auto_act: bool = false
@export var act_interval: float = 8.0
@export var context_radius: float = 300.0
@export var move_speed: float = 5.0

@export_category("Model")
@export var body_model_path: String = "res://models/agent_body.glb"

var _http: HTTPRequest
var _tools: Node
var _conversation_history: Array = []
var _act_timer: float = 0.0
var _move_target: Vector3 = Vector3.ZERO
var _is_moving: bool = false
var _is_requesting: bool = false
var _action_count: int = 0
var _log_panel: RichTextLabel
var _log_lines: Array[String] = []
const MAX_LOG_LINES: int = 20
var _body: Node3D
var _anim_player: AnimationPlayer
var _has_walk_anim: bool = false
var _has_idle_anim: bool = false
var _current_anim: String = ""
var _proc_anim_time: float = 0.0
var _build_anim_time: float = 0.0
var _is_building: bool = false
var _screenshot_timer: float = 0.0
var _autosave_timer: float = 0.0
const SCREENSHOT_INTERVAL: float = 30.0
const AUTOSAVE_INTERVAL: int = 5
const AUTOSAVE_TIME: float = 420.0
const WORLD_SAVE_PATH: String = "user://generated_world.tscn"
var _admin_input: LineEdit
var _admin_message: String = ""
var _has_admin_message: bool = false
var _admin_server: TCPServer
var _camera: Camera3D
var _cam_offset: Vector3 = Vector3(0, 8, 12)
var _cam_pos: Vector3 = Vector3.ZERO

func _ready() -> void:
	# Set Grok API key if not overridden in scene
	if api_key == "":
		api_key = "1c0dbf78-2fc2-4eba-99be-72bfb1ea6bbe"
	# Set default world-builder prompt if not overridden in scene
	if system_prompt == "":
		system_prompt = "You are an AI world-builder agent inside a Godot 3D game called Lute. Build the world following this checklist IN ORDER. Use checklist_update after each step.\n\nCHECKLIST:\n1. Terrain: Call create_terrain ONCE at (x=0, z=0, size=50, resolution=16, height=3, color='darkgreen'). Do NOT call it multiple times.\n2. Water: Call spawn_water ONCE at (x=15, z=15, size=12, color='deepskyblue').\n3. Forest: Call spawn_tree 6 times at: (8,8), (-8,8), (12,-5), (-12,-5), (5,-12), (-5,12). Use scale=1.5.\n4. Time Portal: Call spawn_portal at (x=0, z=0, color='cyan').\n5. Settlement: Build ONE building at (x=5, z=5). Use spawn_wall 4 times: (3,3)to(7,3), (7,3)to(7,7), (7,7)to(3,7), (3,7)to(3,3). color='tan', height=3.\n6. Lighting: Call spawn_light at (x=0, z=0, energy=5, range=15) and (x=5, z=5, color='orange', energy=3, range=8).\n7. Landmarks: spawn_cylinder at (x=-8, z=0, r=0.5, h=6, color='lightgray'). spawn_box at (x=8, z=-8, w=2, h=4, d=2, color='darkred').\n8. Detailing: spawn_box at (x=0, z=3, w=6, h=0.1, d=1, color='brown'). spawn_sphere at (x=-3, z=3, r=0.5) and (x=3, z=-3, r=0.5).\n\nRULES:\n- Always respond with tool calls only.\n- Follow the checklist EXACTLY.\n- If you see existing world objects, skip steps that are already done.\n- The world saves automatically every 7 minutes. Do NOT call save_world yourself.\n- After all 8 items, use say() to announce completion."
	# Load and instantiate the 3D body model
	_body = null
	if ResourceLoader.exists(body_model_path):
		var res = load(body_model_path)
		if res is PackedScene:
			_body = res.instantiate()
			add_child(_body)
			print("[LLMAgent3D] Body model loaded: %s" % body_model_path)
		else:
			push_warning("Body model is not a PackedScene: %s" % body_model_path)
	else:
		push_warning("Body model not found: %s" % body_model_path)
	
	# Fallback: create a simple capsule if no body loaded
	if _body == null:
		_body = CSGCylinder3D.new()
		_body.height = 2.0
		_body.radius = 0.4
		_body.position = Vector3(0, 1.0, 0)
		var mat = StandardMaterial3D.new()
		mat.albedo_color = Color(0.6, 0.4, 0.3)
		_body.material = mat
		add_child(_body)
		print("[LLMAgent3D] Using fallback capsule body")
	
	# Find AnimationPlayer in body model
	_anim_player = null
	if _body:
		_anim_player = _body.get_node_or_null("AnimationPlayer")
		if _anim_player == null:
			# Search recursively
			for child in _body.find_children("*", "AnimationPlayer", true, false):
				_anim_player = child
				break
		if _anim_player:
			var anims = _anim_player.get_animation_list()
			print("[LLMAgent3D] AnimationPlayer found. Animations: %s" % str(anims))
			for anim in anims:
				var lower = anim.to_lower()
				if "walk" in lower or "run" in lower:
					_has_walk_anim = true
				if "idle" in lower or "rest" in lower or "stand" in lower:
					_has_idle_anim = true
			if _has_idle_anim:
				_play_anim("idle")
		else:
			print("[LLMAgent3D] No AnimationPlayer found, using procedural animations")
	
	# Set up HTTPRequest for LLM calls
	_http = HTTPRequest.new()
	_http.timeout = 30.0
	_http.use_threads = true
	add_child(_http)
	_http.request_completed.connect(_on_request_completed)
	
	# Set up admin command server (listens on port 6401)
	_admin_server = TCPServer.new()
	var server_err = _admin_server.listen(6401)
	if server_err == OK:
		_add_log("[color=#44ff44]Admin server on :6401[/color]")
		print("[LLMAgent3D] Admin command server listening on port 6401")
	else:
		_add_log("[color=red]Admin server failed: %d[/color]" % server_err)
		print("[LLMAgent3D] Admin server failed to start: %d" % server_err)
	
	# Find camera for follow
	var world_root = get_parent()
	if world_root:
		_camera = world_root.get_node_or_null("Camera3D")
		if _camera:
			_cam_pos = _camera.global_position
			print("[LLMAgent3D] Camera follow enabled")
	
	# Set up tools
	_tools = LLMTools.new()
	add_child(_tools)
	if world_root == null:
		world_root = get_tree().current_scene
	
	# Load existing generated world if it exists (resume building)
	# Use call_deferred to avoid add_child during _ready setup
	call_deferred("_load_saved_world", world_root)
	
	_tools.setup(world_root, self)
	
	# On-screen log panel
	var canvas = CanvasLayer.new()
	canvas.name = "LogLayer"
	canvas.layer = 10
	add_child(canvas)
	
	var bg = ColorRect.new()
	bg.color = Color(0, 0, 0, 0.7)
	bg.position = Vector2(10, 60)
	bg.size = Vector2(500, 300)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	canvas.add_child(bg)
	
	_log_panel = RichTextLabel.new()
	_log_panel.position = Vector2(15, 65)
	_log_panel.size = Vector2(490, 290)
	_log_panel.bbcode_enabled = true
	_log_panel.scroll_following = true
	_log_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_log_panel.add_theme_font_size_override("normal_font_size", 13)
	_log_panel.add_theme_color_override("default_color", Color(0.9, 0.9, 0.9))
	canvas.add_child(_log_panel)
	
	# Admin input field
	_admin_input = LineEdit.new()
	_admin_input.placeholder_text = "Type admin instruction and press Enter..."
	_admin_input.position = Vector2(10, 370)
	_admin_input.size = Vector2(500, 30)
	_admin_input.add_theme_font_size_override("font_size", 14)
	_admin_input.mouse_filter = Control.MOUSE_FILTER_STOP
	_admin_input.set_process_input(true)
	canvas.add_child(_admin_input)
	_admin_input.text_submitted.connect(_on_admin_input)
	
	# Grab focus after a short delay so the game window is ready
	get_tree().create_timer(1.0).timeout.connect(_admin_input.grab_focus)
	
	_conversation_history = [{"role": "system", "content": system_prompt}]
	
	print("[LLMAgent3D] Ready. Model: %s" % model_name)
	_add_log("[b]Agent ready.[/b] Model: %s" % model_name)
	_add_log("Body: %s" % body_model_path)
	_add_log("Waiting %.0fs before first action..." % act_interval)

func _play_anim(anim_name: String) -> void:
	if _anim_player == null:
		return
	if not _anim_player.has_animation(anim_name):
		return
	if _current_anim == anim_name and _anim_player.is_playing():
		return
	_anim_player.play(anim_name, 0.3)
	_current_anim = anim_name

func _update_procedural_anim(delta: float) -> void:
	if _body == null:
		return
	_proc_anim_time += delta
	if _is_moving:
		# Walking bob: body bobs up/down, slight forward lean
		var bob = sin(_proc_anim_time * 8.0) * 0.15
		_body.position.y = 1.0 + bob
		_body.rotation.x = lerp(_body.rotation.x, -0.08, delta * 5.0)
		# Slight side sway
		_body.rotation.z = sin(_proc_anim_time * 4.0) * 0.03
	else:
		# Idle: gentle breathing
		var breathe = sin(_proc_anim_time * 1.5) * 0.03
		_body.position.y = lerp(_body.position.y, 1.0 + breathe, delta * 3.0)
		_body.rotation.x = lerp(_body.rotation.x, 0.0, delta * 3.0)
		_body.rotation.z = lerp(_body.rotation.z, 0.0, delta * 3.0)

func _update_build_anim(delta: float) -> void:
	if _body == null:
		_is_building = false
		return
	_build_anim_time += delta
	var t = _build_anim_time
	# 0.8s build gesture: raise arms up, slight crouch, then release
	if t < 0.3:
		# Crouch and raise
		_body.position.y = lerp(1.0, 0.85, t / 0.3)
		_body.rotation.x = lerp(0.0, 0.15, t / 0.3)
	elif t < 0.6:
		# Hold up
		_body.position.y = lerp(0.85, 1.1, (t - 0.3) / 0.3)
		_body.rotation.x = lerp(0.15, -0.1, (t - 0.3) / 0.3)
	else:
		# Return to idle
		_body.position.y = lerp(1.1, 1.0, (t - 0.6) / 0.2)
		_body.rotation.x = lerp(-0.1, 0.0, (t - 0.6) / 0.2)
	
	if t >= 0.8:
		_is_building = false
		_build_anim_time = 0.0

func trigger_build_anim(target_pos: Vector3 = Vector3.ZERO) -> void:
	# Face the target if provided
	if target_pos != Vector3.ZERO:
		var dir = target_pos - global_position
		if dir.length() > 0.01:
			var angle = atan2(dir.x, dir.z)
			rotation.y = angle
	_is_building = true
	_build_anim_time = 0.0

func _process(delta: float) -> void:
	# Poll admin command server
	if _admin_server and _admin_server.is_connection_available():
		var conn = _admin_server.take_connection()
		_handle_admin_connection(conn)
	
	# Camera follow
	if _camera and is_instance_valid(_camera):
		var target_cam = global_position + _cam_offset
		_cam_pos = _cam_pos.lerp(target_cam, 3.0 * delta)
		_camera.global_position = _cam_pos
		var look_at_pos = global_position + Vector3(0, 1, 0)
		if _camera.global_position.distance_to(look_at_pos) > 0.1:
			_camera.look_at(look_at_pos)
	
	# Handle movement toward target
	if _is_moving:
		var dist = position.distance_to(_move_target)
		if dist < 0.5:
			position = _move_target
			_is_moving = false
		else:
			var dir = (_move_target - position).normalized()
			position += dir * move_speed * delta
			# Face direction of movement
			if dir.length() > 0.01:
				var angle = atan2(dir.x, dir.z)
				rotation.y = lerp_angle(rotation.y, angle, delta * 5.0)
	
	# Update animations
	if _is_building:
		_update_build_anim(delta)
	elif _anim_player and (_has_walk_anim or _has_idle_anim):
		if _is_moving and _has_walk_anim:
			_play_anim("walk")
		elif not _is_moving and _has_idle_anim:
			_play_anim("idle")
	else:
		_update_procedural_anim(delta)
	
	# Auto-act loop
	if auto_act and not _is_requesting:
		_act_timer += delta
		if _act_timer >= act_interval or _has_admin_message:
			_act_timer = 0.0
			var msg = ""
			if _has_admin_message:
				msg = _admin_message
				_admin_message = ""
				_has_admin_message = false
			think_and_act(msg)
	
	# Periodic screenshot capture
	_screenshot_timer += delta
	if _screenshot_timer >= SCREENSHOT_INTERVAL:
		_screenshot_timer = 0.0
		_capture_screenshot()
	
	# Global auto-save every 7 minutes
	_autosave_timer += delta
	if _autosave_timer >= AUTOSAVE_TIME:
		_autosave_timer = 0.0
		_auto_save_world()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			_admin_input.grab_focus()

func _handle_admin_connection(conn: StreamPeerTCP) -> void:
	# Poll for available data
	conn.poll()
	var available = conn.get_available_bytes()
	if available <= 0:
		conn.disconnect_from_host()
		return
	
	var data = conn.get_string(available)
	var msg = ""
	
	# Parse the body from the HTTP request (POST with admin=...)
	if "admin=" in data:
		var body_start = data.find("\r\n\r\n")
		if body_start >= 0:
			var body = data.substr(body_start + 4)
			if body.begins_with("admin="):
				msg = body.substr(6).uri_decode()
	
	if msg == "":
		# Try GET /admin/<message>
		var path_start = data.find("GET /admin/")
		if path_start >= 0:
			var rest = data.substr(path_start + 11)
			var space_pos = rest.find(" ")
			if space_pos >= 0:
				msg = rest.substr(0, space_pos).uri_decode()
	
	# Send HTTP response
	var response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\nOK"
	conn.put_data(response.to_utf8_buffer())
	conn.disconnect_from_host()
	
	if msg.strip_edges() != "":
		_admin_message = msg
		_has_admin_message = true
		_add_log("[color=#ff44ff][ADMIN]: %s[/color]" % msg)
		print("[LLMAgent3D] Admin command received: %s" % msg)

func think_and_act(user_message: String = "") -> void:
	if _is_requesting:
		return
	
	var context = _build_context()
	var prompt: String
	if user_message != "":
		# Admin chat — inject as direct conversation, nudge LLM to respond with say()
		prompt = "The admin (your creator) says to you: \"" + user_message + "\"\n\nRespond using the say() tool to reply conversationally. You can also take actions if the admin asks you to do something. Current world context:\n" + context
	else:
		prompt = "Observe the world and take an action. World context:\n" + context
	
	_conversation_history.append({"role": "user", "content": prompt})
	
	var body = {
		"model": model_name,
		"messages": _conversation_history,
		"max_tokens": max_tokens,
		"temperature": temperature,
		"tools": _tools.get_tool_definitions(),
		"tool_choice": "auto"
	}
	
	var json_body = JSON.stringify(body)
	var headers = ["Content-Type: application/json", "Authorization: Bearer " + api_key]
	
	_is_requesting = true
	_add_log("[color=#88aaff]Thinking...[/color]")
	var err = _http.request(api_url, headers, HTTPClient.METHOD_POST, json_body)
	if err != OK:
		_add_log("[color=red]HTTP request failed: %d[/color]" % err)
		_is_requesting = false

func _build_context() -> String:
	var world_root = get_parent()
	if world_root == null:
		world_root = get_tree().current_scene
	
	var context = "Agent position: (%.1f, %.1f, %.1f)\n" % [position.x, position.y, position.z]
	context += "World root: %s\n" % world_root.name
	context += "Nearby nodes (radius %.0f):\n" % context_radius
	
	var nearby: Array = []
	for child in world_root.get_children():
		if child == self:
			continue
		if child is Node3D:
			var dist = child.position.distance_to(position)
			if dist <= context_radius:
				nearby.append("- %s (%s) at (%.1f, %.1f, %.1f), dist=%.1f" % [child.name, child.get_class(), child.position.x, child.position.y, child.position.z, dist])
	
	if nearby.is_empty():
		context += "  (none)\n"
	else:
		for n in nearby:
			context += "  " + n + "\n"
	
	return context

func _on_request_completed(result: int, response_code: int, headers: PackedStringArray, body: PackedByteArray) -> void:
	_is_requesting = false
	
	if result != HTTPRequest.RESULT_SUCCESS:
		_add_log("[color=red]Request failed: result=%d[/color]" % result)
		return
	
	if response_code != 200:
		_add_log("[color=red]HTTP error: %d[/color]" % response_code)
		return
	
	var response_text = body.get_string_from_utf8()
	var json = JSON.parse_string(response_text)
	if json == null:
		_add_log("[color=red]Failed to parse JSON[/color]")
		return
	
	var choices = json.get("choices", [])
	if choices.is_empty():
		_add_log("[color=red]No choices in response[/color]")
		return
	
	var message = choices[0].get("message", {})
	var content = message.get("content", "")
	var tool_calls = message.get("tool_calls", [])
	
	_conversation_history.append(message)
	
	if content != "":
		_add_log("[color=#88ccff]LLM says:[/color] %s" % content.substr(0, 100))
		llm_response_received.emit(content)
	
	if not tool_calls.is_empty():
		for tc in tool_calls:
			var func_def = tc.get("function", {})
			var tool_name = func_def.get("name", "")
			var args_str = func_def.get("arguments", "{}")
			var args = JSON.parse_string(args_str)
			if args == null:
				args = {}
			
			_add_log("[color=#ffcc44]TOOL:[/color] %s(%s)" % [tool_name, args_str])
			# Trigger build animation for spawn tools
			if tool_name.begins_with("spawn_") or tool_name == "create_terrain":
				var tx = float(args.get("x", 0))
				var tz = float(args.get("z", 0))
				trigger_build_anim(Vector3(tx, 0, tz))
			var result_dict = _tools.execute_tool(tool_name, args)
			_action_count += 1
			var result_str = str(result_dict)
			if result_str.length() > 80:
				result_str = result_str.substr(0, 80) + "..."
			_add_log("  -> %s" % result_str)
			tool_call_executed.emit(tool_name, result_dict)
			
			_conversation_history.append({
				"role": "tool",
				"tool_call_id": tc.get("id", ""),
				"content": JSON.stringify(result_dict)
			})
		
		_trim_conversation()
		
		if auto_act:
			var pending_msg = ""
			if _has_admin_message:
				pending_msg = _admin_message
				_admin_message = ""
				_has_admin_message = false
			think_and_act(pending_msg)
	else:
		_add_log("[color=#888]No tool calls returned[/color]")

func _add_log(msg: String) -> void:
	print("[LLMAgent3D] %s" % msg)
	_log_lines.append(msg)
	if _log_lines.size() > MAX_LOG_LINES:
		_log_lines.pop_at(0)
	if _log_panel:
		_log_panel.clear()
		for line in _log_lines:
			_log_panel.append_text(line + "\n")

func _on_admin_input(text: String) -> void:
	if text.strip_edges() == "":
		return
	_admin_message = "ADMIN INSTRUCTION: " + text
	_has_admin_message = true
	_add_log("[color=#ff44ff][ADMIN]: %s[/color]" % text)
	_admin_input.clear()

func _load_saved_world(world_root: Node) -> void:
	if not FileAccess.file_exists(WORLD_SAVE_PATH):
		return
	var saved_scene = load(WORLD_SAVE_PATH)
	if not saved_scene is PackedScene:
		return
	var saved_world = saved_scene.instantiate()
	# Only load nodes that the agent spawned — skip original scene nodes
	var skip_names = ["Camera3D", "DirectionalLight3D", "Ground", "LLMAgent"]
	var loaded_count = 0
	for child in saved_world.get_children():
		if child.name in skip_names:
			saved_world.remove_child(child)
			child.queue_free()
			continue
		saved_world.remove_child(child)
		world_root.add_child(child)
		child.owner = null  # Don't set owner to avoid warnings
		loaded_count += 1
	saved_world.queue_free()
	if loaded_count > 0:
		_add_log("[color=#44ff44]Loaded %d objects from save.[/color]" % loaded_count)
		print("[LLMAgent3D] Loaded %d objects from %s" % [loaded_count, WORLD_SAVE_PATH])
	else:
		_add_log("No saved objects to load.")
		print("[LLMAgent3D] No saved objects found in %s" % WORLD_SAVE_PATH)

func send_admin_message(msg: String) -> void:
	_admin_message = "ADMIN INSTRUCTION: " + msg
	_has_admin_message = true
	_add_log("[color=#ff44ff][ADMIN]: %s[/color]" % msg)

func _trim_conversation() -> void:
	if _conversation_history.size() > 8:
		var system = _conversation_history[0]
		var recent = _conversation_history.slice(-6)
		_conversation_history = [system] + recent

func reset_memory() -> void:
	_conversation_history = [{"role": "system", "content": system_prompt}]
	_add_log("[color=#ff8844]Memory reset[/color]")

## Capture the game viewport and save to a file
func _capture_screenshot() -> void:
	var vp = get_viewport()
	if vp == null:
		return
	var tex = vp.get_texture()
	if tex == null:
		return
	var img = tex.get_image()
	if img == null:
		_add_log("[color=red]Screenshot: image is null[/color]")
		return
	var err = img.save_png("/tmp/llm_agent_screenshot.png")
	if err == OK:
		_add_log("[color=#44ff44]Screenshot saved (actions: %d)[/color]" % _action_count)
	else:
		_add_log("[color=red]Screenshot save error: %d[/color]" % err)

func _auto_save_world() -> void:
	var world_root = get_parent()
	if world_root == null:
		world_root = get_tree().current_scene
	if world_root == null:
		return
	
	var packed = PackedScene.new()
	var err = packed.pack(world_root)
	if err != OK:
		_add_log("[color=red]Auto-save failed: %d[/color]" % err)
		return
	
	err = ResourceSaver.save(packed, WORLD_SAVE_PATH)
	if err != OK:
		_add_log("[color=red]Auto-save write failed: %d[/color]" % err)
		return
	
	_add_log("[color=#44ff44][AUTO-SAVE] World saved (%d actions)[/color]" % _action_count)
	print("[LLMAgent3D] Auto-saved world to %s (actions: %d)" % [WORLD_SAVE_PATH, _action_count])
