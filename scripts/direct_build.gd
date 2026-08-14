extends Node
## Direct world builder — bypasses LLM, calls LLMTools functions directly.
## Triggered by admin command "direct_build" in llm_agent_3d.gd.

var _tools: Node = null
var _world_root: Node = null
var _step: int = 0
var _steps: Array = []
var _timer: Timer = null
var _done: bool = false

func build(tools: Node, world_root: Node) -> void:
	_tools = tools
	_world_root = world_root
	_step = 0
	_done = false
	
	# Clear existing world objects (keep camera, light, ground, agent)
	var skip = ["Camera3D", "DirectionalLight3D", "Ground", "LLMAgent", "LogLayer"]
	for child in _world_root.get_children():
		if child.name in skip:
			continue
		child.queue_free()
	
	# Build step queue — each step is [tool_name, args_dict, description]
	_steps = _build_steps()
	
	# Create timer for staggered execution (avoids blocking main thread)
	_timer = Timer.new()
	_timer.wait_time = 0.05
	_timer.autostart = true
	_timer.timeout.connect(_next_step)
	add_child(_timer)
	
	print("[DirectBuild] Starting direct world build: %d steps" % _steps.size())

func _next_step() -> void:
	if _step >= _steps.size():
		if not _done:
			_done = true
			_timer.stop()
			_timer.queue_free()
			print("[DirectBuild] BUILD COMPLETE — %d steps executed" % _steps.size())
			# Save world
			_tools.execute_tool("save_world", {})
			_tools.execute_tool("save_state", {})
		return
	
	var step = _steps[_step]
	var tool_name = step[0]
	var args = step[1]
	var desc = step[2]
	
	var result = _tools.execute_tool(tool_name, args)
	if result.has("error"):
		print("[DirectBuild] ERROR step %d (%s): %s" % [_step, desc, result["error"]])
	else:
		print("[DirectBuild] Step %d/%d: %s ✓" % [_step + 1, _steps.size(), desc])
	
	_step += 1

func _build_steps() -> Array:
	var s: Array = []
	
	# PHASE 1: Terrain
	s.append(["create_terra_terrain", {"size": 128.0, "height": 20.0, "texture_set": "grass", "noise_scale": 0.008}, "Terrain3D heightmap"])
	
	# PHASE 2: Water
	s.append(["spawn_water", {"x": 30.0, "z": -20.0, "size": 30.0, "color": "deepblue"}, "Lake 1"])
	s.append(["spawn_water", {"x": -30.0, "z": 25.0, "size": 20.0, "color": "deepblue"}, "Lake 2"])
	
	# PHASE 3: Forests (133 trees)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_1.fbx", "center_x": 20.0, "center_z": 20.0, "radius": 50.0, "count": 30, "min_scale": 1.0, "max_scale": 2.5}, "Trees: CommonTree_1 x30"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_2.fbx", "center_x": -20.0, "center_z": 15.0, "radius": 45.0, "count": 25, "min_scale": 0.8, "max_scale": 2.0}, "Trees: CommonTree_2 x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_3.fbx", "center_x": 15.0, "center_z": -25.0, "radius": 40.0, "count": 20, "min_scale": 1.0, "max_scale": 2.2}, "Trees: CommonTree_3 x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pine_1.fbx", "center_x": -25.0, "center_z": -20.0, "radius": 45.0, "count": 25, "min_scale": 0.8, "max_scale": 1.8}, "Trees: Pine_1 x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pine_2.fbx", "center_x": 30.0, "center_z": -15.0, "radius": 35.0, "count": 15, "min_scale": 0.8, "max_scale": 1.6}, "Trees: Pine_2 x15"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/TwistedTree_1.fbx", "center_x": -15.0, "center_z": 30.0, "radius": 30.0, "count": 12, "min_scale": 1.0, "max_scale": 2.0}, "Trees: TwistedTree_1 x12"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/DeadTree_1.fbx", "center_x": 25.0, "center_z": 25.0, "radius": 25.0, "count": 6, "min_scale": 1.0, "max_scale": 1.8}, "Trees: DeadTree_1 x6"])
	
	# PHASE 4: Rocks (70)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_1.fbx", "center_x": 35.0, "center_z": -30.0, "radius": 30.0, "count": 12, "min_scale": 0.5, "max_scale": 2.0}, "Rocks: Rock_Medium_1 x12"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_2.fbx", "center_x": -35.0, "center_z": 20.0, "radius": 25.0, "count": 10, "min_scale": 0.5, "max_scale": 1.8}, "Rocks: Rock_Medium_2 x10"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_3.fbx", "center_x": 10.0, "center_z": 35.0, "radius": 20.0, "count": 8, "min_scale": 0.5, "max_scale": 1.5}, "Rocks: Rock_Medium_3 x8"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pebble_Round_1.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 20, "min_scale": 0.2, "max_scale": 0.6}, "Rocks: Pebble_Round_1 x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pebble_Round_2.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 20, "min_scale": 0.2, "max_scale": 0.6}, "Rocks: Pebble_Round_2 x20"])
	
	# PHASE 5: Ground cover (68)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Bush_Common.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 25, "min_scale": 0.5, "max_scale": 1.2}, "Bushes x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Fern_1.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 20, "min_scale": 0.4, "max_scale": 0.8}, "Ferns x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Flower_4_Group.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 50.0, "count": 15, "min_scale": 0.4, "max_scale": 0.8}, "Flowers x15"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Mushroom_Laetiporus.fbx", "center_x": -20.0, "center_z": 20.0, "radius": 25.0, "count": 8, "min_scale": 0.3, "max_scale": 0.6}, "Mushrooms x8"])
	
	# PHASE 6: Monuments — Industrial cluster (north-east)
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-a.glb", "x": 40.0, "z": -40.0, "scale": 1.2}, "Building A"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-b.glb", "x": 45.0, "z": -35.0, "scale": 1.0}, "Building B"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/chimney-medium.glb", "x": 38.0, "z": -42.0, "scale": 0.8}, "Chimney Medium"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/detail-tank.glb", "x": 48.0, "z": -38.0, "scale": 0.6}, "Detail Tank"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": 42.0, "z": -38.0, "scale": 0.5}, "Crate 1"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": 44.0, "z": -36.0, "scale": 0.5}, "Crate 2"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": 36.0, "z": -40.0, "scale": 0.7}, "Fence 1"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": 50.0, "z": -40.0, "scale": 0.7}, "Fence 2"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Stairs_Exterior_Sides.fbx", "x": 43.0, "z": -33.0, "scale": 0.6}, "Stairs 1"])
	
	# PHASE 6b: Gas Station cluster (south-west)
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-c.glb", "x": -40.0, "z": 30.0, "scale": 1.0}, "Building C"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/chimney-small.glb", "x": -42.0, "z": 28.0, "scale": 0.6}, "Chimney Small"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": -38.0, "z": 32.0, "scale": 0.5}, "Crate 3"])
	
	# PHASE 7: Roads
	s.append(["spawn_box", {"x": 0.0, "z": -5.0, "w": 60.0, "h": 0.2, "d": 4.0, "color": "#3a3a3a"}, "Road 1 (east-west)"])
	s.append(["spawn_box", {"x": 20.0, "z": -20.0, "w": 4.0, "h": 0.2, "d": 30.0, "color": "#3a3a3a"}, "Road 2 (to industrial)"])
	s.append(["spawn_box", {"x": -20.0, "z": 15.0, "w": 4.0, "h": 0.2, "d": 30.0, "color": "#3a3a3a"}, "Road 3 (to gas station)"])
	
	# PHASE 8: Lights
	s.append(["spawn_light", {"x": 0.0, "z": 0.0, "energy": 10.0, "range": 40.0, "color": "white"}, "Light center"])
	s.append(["spawn_light", {"x": 40.0, "z": -40.0, "energy": 5.0, "range": 20.0, "color": "warmyellow"}, "Light industrial"])
	s.append(["spawn_light", {"x": -40.0, "z": 30.0, "energy": 4.0, "range": 15.0, "color": "orange"}, "Light gas station"])
	s.append(["spawn_light", {"x": 20.0, "z": 20.0, "energy": 3.0, "range": 12.0, "color": "orange"}, "Light forest edge 1"])
	s.append(["spawn_light", {"x": -20.0, "z": -20.0, "energy": 3.0, "range": 12.0, "color": "orange"}, "Light forest edge 2"])
	
	# PHASE 9: PBR Materials
	s.append(["apply_material", {"model_name": "CommonTree", "texture_set": "grass"}, "PBR: Trees"])
	s.append(["apply_material", {"model_name": "Pine", "texture_set": "grass"}, "PBR: Pines"])
	s.append(["apply_material", {"model_name": "TwistedTree", "texture_set": "grass"}, "PBR: TwistedTrees"])
	s.append(["apply_material", {"model_name": "DeadTree", "texture_set": "grass"}, "PBR: DeadTrees"])
	s.append(["apply_material", {"model_name": "Rock", "texture_set": "rock"}, "PBR: Rocks"])
	s.append(["apply_material", {"model_name": "Pebble", "texture_set": "rock"}, "PBR: Pebbles"])
	s.append(["apply_material", {"model_name": "Bush", "texture_set": "grass"}, "PBR: Bushes"])
	s.append(["apply_material", {"model_name": "Fern", "texture_set": "grass"}, "PBR: Ferns"])
	s.append(["apply_material", {"model_name": "Flower", "texture_set": "grass"}, "PBR: Flowers"])
	s.append(["apply_material", {"model_name": "Mushroom", "texture_set": "ground_dirt"}, "PBR: Mushrooms"])
	s.append(["apply_material", {"model_name": "building", "texture_set": "rock"}, "PBR: Buildings"])
	s.append(["apply_material", {"model_name": "chimney", "texture_set": "rock"}, "PBR: Chimneys"])
	
	return s
