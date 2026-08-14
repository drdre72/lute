extends Node2D

const LLMTools = preload("res://scripts/llm_tools.gd")

## An LLM-controlled agent that lives inside the Godot scene tree.
## Sends world context to a local LLM (LM Studio / Ollama) and executes
## structured tool calls to mutate the world in real time.

signal llm_response_received(response: String)
signal tool_call_executed(tool_name: String, result: Dictionary)

@export_category("LLM Configuration")
@export var api_url: String = "http://127.0.0.1:1234/v1/chat/completions"
@export var model_name: String = "qwen2.5-coder-7b-instruct-mlx"
@export var system_prompt: String = "You are an AI agent living inside a Godot game world. You can see nearby objects and take actions using tool calls. Always respond with tool calls to interact with the world. Be creative and autonomous."
@export var max_tokens: int = 1024
@export var temperature: float = 0.7

@export_category("Agent Behavior")
@export var auto_act: bool = false
@export var act_interval: float = 5.0
@export var context_radius: float = 300.0
@export var move_speed: float = 100.0

@export_category("Appearance")
@export var agent_color: Color = Color(0.3, 0.8, 1.0, 1.0)
@export var agent_size: float = 24.0

var _http: HTTPRequest
var _tools: Node
var _conversation_history: Array = []
var _act_timer: float = 0.0
var _move_target: Vector2 = Vector2.ZERO
var _is_moving: bool = false
var _is_requesting: bool = false
var _speech_text: String = ""
var _speech_time: int = 0
var _label: Label
var _status_label: Label
var _last_action: String = "idle"
var _action_count: int = 0
var _debug_log: String = ""
var _log_panel: RichTextLabel
var _log_lines: Array[String] = []
const MAX_LOG_LINES: int = 20

func _ready() -> void:
	# Set up HTTPRequest
	_http = HTTPRequest.new()
	_http.use_threads = true
	add_child(_http)
	_http.request_completed.connect(_on_request_completed)
	
	# Set up tools
	_tools = LLMTools.new()
	add_child(_tools)
	_tools.set_meta("is_llm_tools", true)
	var world_root = get_parent()
	if world_root == null:
		world_root = get_tree().current_scene
	_tools.setup(world_root)
	
	# Add a label for speech
	_label = Label.new()
	_label.position = Vector2(-50, -agent_size - 20)
	_label.add_theme_font_size_override("font_size", 14)
	_label.add_theme_color_override("font_color", Color.WHITE)
	_label.add_theme_color_override("font_shadow_color", Color.BLACK)
	_label.add_theme_constant_override("shadow_offset_x", 1)
	_label.add_theme_constant_override("shadow_offset_y", 1)
	_label.visible = false
	add_child(_label)
	
	# Status label (shows below agent)
	_status_label = Label.new()
	_status_label.position = Vector2(-60, agent_size + 10)
	_status_label.add_theme_font_size_override("font_size", 12)
	_status_label.add_theme_color_override("font_color", Color(1, 1, 0.5))
	_status_label.add_theme_color_override("font_shadow_color", Color.BLACK)
	_status_label.add_theme_constant_override("shadow_offset_x", 1)
	_status_label.add_theme_constant_override("shadow_offset_y", 1)
	_status_label.text = "idle"
	add_child(_status_label)
	
	# On-screen log panel (CanvasLayer + RichTextLabel)
	var canvas = CanvasLayer.new()
	canvas.name = "LogLayer"
	canvas.layer = 10
	add_child(canvas)
	
	var bg = ColorRect.new()
	bg.color = Color(0, 0, 0, 0.7)
	bg.position = Vector2(10, 60)
	bg.size = Vector2(500, 300)
	canvas.add_child(bg)
	
	_log_panel = RichTextLabel.new()
	_log_panel.position = Vector2(15, 65)
	_log_panel.size = Vector2(490, 290)
	_log_panel.bbcode_enabled = true
	_log_panel.scroll_following = true
	_log_panel.add_theme_font_size_override("normal_font_size", 13)
	_log_panel.add_theme_color_override("default_color", Color(0.9, 0.9, 0.9))
	canvas.add_child(_log_panel)
	
	# Initialize conversation with system prompt
	_conversation_history = [{"role": "system", "content": system_prompt}]
	_debug_log = "/tmp/llm_agent_debug.log"
	
	print("[LLMAgent] Ready. Model: %s" % model_name)
	_add_log("[b]Agent ready.[/b] Model: %s" % model_name)
	_add_log("Waiting %.0fs before first action..." % act_interval)

func _process(delta: float) -> void:
	# Handle movement toward target
	if _is_moving:
		var dist = position.distance_to(_move_target)
		if dist < 5.0:
			position = _move_target
			_is_moving = false
		else:
			var dir = (_move_target - position).normalized()
			var speed = move_speed * float(get_meta("move_speed", 1.0))
			position += dir * speed * delta
	
	# Handle speech bubble timeout
	if _speech_time > 0 and Time.get_ticks_msec() - _speech_time > 5000:
		_speech_text = ""
		_speech_time = 0
		if _label:
			_label.visible = false
	
	# Auto-act loop — continuous building
	if auto_act and not _is_requesting:
		_act_timer += delta
		if _act_timer >= act_interval:
			_act_timer = 0.0
			think_and_act()
	
	# Update status label
	if _status_label:
		var status = "idle"
		if _is_requesting:
			status = "thinking..."
		elif _is_moving:
			status = "moving"
		_status_label.text = "actions:%d | %s" % [_action_count, status]

func _draw() -> void:
	# Draw the agent as a colored circle
	draw_circle(Vector2.ZERO, agent_size, agent_color)
	draw_arc(Vector2.ZERO, agent_size, 0, TAU, 32, Color.WHITE, 2.0)
	
	# Draw eyes
	draw_circle(Vector2(-agent_size * 0.3, -agent_size * 0.1), agent_size * 0.12, Color.WHITE)
	draw_circle(Vector2(agent_size * 0.3, -agent_size * 0.1), agent_size * 0.12, Color.WHITE)
	draw_circle(Vector2(-agent_size * 0.3, -agent_size * 0.1), agent_size * 0.06, Color.BLACK)
	draw_circle(Vector2(agent_size * 0.3, -agent_size * 0.1), agent_size * 0.06, Color.BLACK)
	
	# Draw move target indicator
	if _is_moving:
		draw_arc(_move_target - position, 8, 0, TAU, 16, Color(1, 1, 0, 0.5), 1.5)

## Manually trigger the agent to observe the world and take action
func think_and_act(user_message: String = "") -> void:
	if _is_requesting:
		print("[LLMAgent] Already processing a request, skipping...")
		return
	
	# Build context
	var context = _build_context()
	var prompt: String
	if user_message != "":
		prompt = user_message + "\n\nWorld context:\n" + context
	else:
		prompt = "Observe the world and take an action. World context:\n" + context
	
	_conversation_history.append({"role": "user", "content": prompt})
	
	# Build request body with tools
	var body = {
		"model": model_name,
		"messages": _conversation_history,
		"max_tokens": max_tokens,
		"temperature": temperature,
		"tools": _tools.get_tool_definitions(),
		"tool_choice": "auto"
	}
	
	var json_body = JSON.stringify(body)
	var headers = ["Content-Type: application/json"]
	
	_is_requesting = true
	print("[LLMAgent] Sending request to %s ..." % api_url)
	_log_debug("Sending request to LLM...")
	var err = _http.request(api_url, headers, HTTPClient.METHOD_POST, json_body)
	if err != OK:
		print("[LLMAgent] HTTP request failed: %d" % err)
		_is_requesting = false
		_log_debug("HTTP request failed: %d" % err)

func _build_context() -> String:
	var world_root = get_parent()
	if world_root == null:
		world_root = get_tree().current_scene
	
	var context = "Agent position: (%.1f, %.1f)\n" % [position.x, position.y]
	context += "World root: %s\n" % world_root.name
	context += "Nearby nodes (radius %.0f):\n" % context_radius
	
	var nearby: Array = []
	for child in world_root.get_children():
		if child == self:
			continue
		if child is Node2D:
			var dist = child.position.distance_to(position)
			if dist <= context_radius:
				nearby.append("- %s (%s) at (%.1f, %.1f), dist=%.1f" % [child.name, child.get_class(), child.position.x, child.position.y, dist])
	
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
	
	# Add assistant response to conversation
	_conversation_history.append(message)
	
	if content != "":
		_add_log("[color=#88ccff]LLM says:[/color] %s" % content.substr(0, 100))
		llm_response_received.emit(content)
	
	# Execute tool calls
	if not tool_calls.is_empty():
		for tc in tool_calls:
			var func_def = tc.get("function", {})
			var tool_name = func_def.get("name", "")
			var args_str = func_def.get("arguments", "{}")
			var args = JSON.parse_string(args_str)
			if args == null:
				args = {}
			
			_add_log("[color=#ffcc44]TOOL:[/color] %s(%s)" % [tool_name, args_str])
			var result_dict = _tools.execute_tool(tool_name, args)
			_action_count += 1
			var result_str = str(result_dict)
			if result_str.length() > 80:
				result_str = result_str.substr(0, 80) + "..."
			_add_log("  -> %s" % result_str)
			tool_call_executed.emit(tool_name, result_dict)
			
			# Add tool result to conversation
			_conversation_history.append({
				"role": "tool",
				"tool_call_id": tc.get("id", ""),
				"content": JSON.stringify(result_dict)
			})
		
		# Trim conversation to prevent unbounded growth (keep system + last 10 messages)
		_trim_conversation()
		
		# Continuous loop — immediately think and act again
		if auto_act:
			think_and_act()
	else:
		_add_log("[color=#888]No tool calls returned[/color]")
	
	queue_redraw()

## Get current speech text for drawing
func get_speech() -> String:
	if _speech_time > 0 and Time.get_ticks_msec() - _speech_time < 5000:
		return _speech_text
	return ""

## Clear conversation history (keep system prompt)
func reset_memory() -> void:
	_conversation_history = [{"role": "system", "content": system_prompt}]
	print("[LLMAgent] Memory reset")

## Add a line to the on-screen log panel
func _add_log(msg: String) -> void:
	print("[LLMAgent] %s" % msg)
	_log_lines.append(msg)
	if _log_lines.size() > MAX_LOG_LINES:
		_log_lines.pop_at(0)
	if _log_panel:
		_log_panel.clear()
		for line in _log_lines:
			_log_panel.append_text(line + "\n")

## Trim conversation history to prevent token overflow
func _trim_conversation() -> void:
	if _conversation_history.size() > 12:
		var system = _conversation_history[0]
		var recent = _conversation_history.slice(-10)
		_conversation_history = [system] + recent
