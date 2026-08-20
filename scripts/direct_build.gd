extends Node
## Direct world builder v2 — bypasses LLM, calls LLMTools functions directly.
## Adds Sky3D, WorldEnvironment, SimpleGrassTextured, 2x density, all buildings.
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
	
	# Add Sky3D, WorldEnvironment, and SimpleGrassTextured directly (not via tools)
	_add_sky3d()
	_add_world_environment()
	_add_simple_grass()
	
	# Build step queue — each step is [tool_name, args_dict, description]
	_steps = _build_steps()
	
	# Create timer for staggered execution (avoids blocking main thread)
	_timer = Timer.new()
	_timer.wait_time = 0.05
	_timer.autostart = true
	_timer.timeout.connect(_next_step)
	add_child(_timer)
	
	print("[DirectBuild] Starting direct world build v2: %d steps" % _steps.size())

func _next_step() -> void:
	if _step >= _steps.size():
		if not _done:
			_done = true
			_timer.stop()
			_timer.queue_free()
			print("[DirectBuild] BUILD COMPLETE — %d steps executed" % _steps.size())
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

func _add_sky3d() -> void:
	# Add Sky3D for dynamic day/night sky
	var sky3d_script = load("res://addons/sky_3d/src/Sky3D.gd")
	var time_script = load("res://addons/sky_3d/src/TimeOfDay.gd")
	var dome_script = load("res://addons/sky_3d/src/SkyDome.gd")
	if sky3d_script == null:
		print("[DirectBuild] Sky3D scripts not found, skipping")
		return
	
	var sky3d = WorldEnvironment.new()
	sky3d.name = "Sky3D"
	sky3d.set_script(sky3d_script)
	sky3d.current_time = 8.0
	sky3d.wind_speed = 5.0
	
	# Create environment
	var env = Environment.new()
	env.background_mode = Environment.BG_SKY
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_color = Color(0.4, 0.5, 0.6)
	env.ambient_light_energy = 0.3
	env.fog_enabled = true
	env.fog_light_color = Color(0.5, 0.6, 0.7, 0.8)
	env.fog_light_energy = 0.4
	env.fog_density = 0.005
	env.fog_aerial_perspective = 0.5
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.glow_enabled = true
	env.glow_intensity = 0.8
	env.glow_strength = 1.0
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_ADDITIVE
	sky3d.environment = env
	
	# Camera attributes for depth of field
	var cam_attrs = CameraAttributesPractical.new()
	cam_attrs.exposure_multiplier = 1.0
	cam_attrs.auto_exposure_enabled = true
	cam_attrs.auto_exposure_scale = 0.4
	sky3d.camera_attributes = cam_attrs
	
	_world_root.add_child(sky3d)
	
	# Sun light
	var sun = DirectionalLight3D.new()
	sun.name = "SunLight"
	sun.light_color = Color(0.98, 0.52, 0.29, 1)
	sun.light_energy = 1.2
	sun.directional_shadow_blend_splits = true
	sun.directional_shadow_max_distance = 600.0
	sky3d.add_child(sun)
	
	# Moon light
	var moon = DirectionalLight3D.new()
	moon.name = "MoonLight"
	moon.light_color = Color(0.57, 0.78, 0.96, 1)
	moon.light_energy = 0.0
	moon.directional_shadow_blend_splits = true
	moon.directional_shadow_max_distance = 256.0
	sky3d.add_child(moon)
	
	# Sky dome
	var dome = Node.new()
	dome.name = "SkyDome"
	dome.set_script(dome_script)
	sky3d.add_child(dome)
	
	# Time of day controller
	var tod = Node.new()
	tod.name = "TimeOfDay"
	tod.set_script(time_script)
	tod.dome_path = NodePath("../SkyDome")
	sky3d.add_child(tod)
	
	print("[DirectBuild] Sky3D added with day/night cycle")

func _add_world_environment() -> void:
	# Check if Sky3D already added environment
	var existing_env = _world_root.get_node_or_null("Sky3D")
	if existing_env and existing_env is WorldEnvironment:
		print("[DirectBuild] WorldEnvironment already set via Sky3D")
		return
	
	# Fallback: basic WorldEnvironment
	var we = WorldEnvironment.new()
	we.name = "WorldEnvironment"
	var env = Environment.new()
	env.background_mode = Environment.BG_SKY_COLOR
	env.background_color = Color(0.3, 0.5, 0.8)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_color = Color(0.4, 0.5, 0.6)
	env.ambient_light_energy = 0.3
	env.fog_enabled = true
	env.fog_light_color = Color(0.5, 0.6, 0.7, 0.8)
	env.fog_density = 0.005
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.glow_enabled = true
	env.glow_intensity = 0.8
	we.environment = env
	_world_root.add_child(we)
	print("[DirectBuild] WorldEnvironment added (fallback)")

func _add_simple_grass() -> void:
	# Add SimpleGrassTextured for GPU-rendered grass with wind
	var grass_script = load("res://addons/simplegrasstextured/grass.gd")
	if grass_script == null:
		print("[DirectBuild] SimpleGrassTextured not found, skipping")
		return
	
	var grass = MultiMeshInstance3D.new()
	grass.name = "SimpleGrassTextured"
	grass.set_script(grass_script)
	grass.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	grass.layers = 1 << 16  # layer 17 for grass
	
	# Load grass texture
	var grass_tex = load("res://textures/pbr/Grass006_1K/Grass006_1K-JPG_Color.jpg")
	if grass_tex:
		var mat = StandardMaterial3D.new()
		mat.albedo_texture = grass_tex
		mat.albedo_color = Color(0.4, 0.6, 0.3)
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
		mat.cull_mode = BaseMaterial3D.CULL_MODE_DISABLED
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		grass.material_override = mat
	
	_world_root.add_child(grass)
	print("[DirectBuild] SimpleGrassTextured added")

func _build_steps() -> Array:
	var s: Array = []
	
	# PHASE 1: Terrain — taller hills, more variation
	s.append(["create_terra_terrain", {"size": 128.0, "height": 25.0, "texture_set": "grass", "noise_scale": 0.005}, "Terrain3D heightmap (h=25)"])
	
	# PHASE 2: Water — 3 lakes of varying sizes
	s.append(["spawn_water", {"x": 30.0, "z": -20.0, "size": 35.0, "color": "deepblue"}, "Lake 1 (large)"])
	s.append(["spawn_water", {"x": -30.0, "z": 25.0, "size": 22.0, "color": "deepblue"}, "Lake 2 (medium)"])
	s.append(["spawn_water", {"x": -5.0, "z": 45.0, "size": 12.0, "color": "deepblue"}, "Lake 3 (small pond)"])
	
	# PHASE 3: Forests (266 trees — 2x density)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_1.fbx", "center_x": 20.0, "center_z": 20.0, "radius": 55.0, "count": 50, "min_scale": 1.0, "max_scale": 2.5}, "Trees: CommonTree_1 x50"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_2.fbx", "center_x": -20.0, "center_z": 15.0, "radius": 50.0, "count": 40, "min_scale": 0.8, "max_scale": 2.0}, "Trees: CommonTree_2 x40"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_3.fbx", "center_x": 15.0, "center_z": -25.0, "radius": 45.0, "count": 35, "min_scale": 1.0, "max_scale": 2.2}, "Trees: CommonTree_3 x35"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_4.fbx", "center_x": -30.0, "center_z": -30.0, "radius": 40.0, "count": 25, "min_scale": 0.8, "max_scale": 1.8}, "Trees: CommonTree_4 x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/CommonTree_5.fbx", "center_x": 35.0, "center_z": 10.0, "radius": 35.0, "count": 20, "min_scale": 0.8, "max_scale": 1.6}, "Trees: CommonTree_5 x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pine_1.fbx", "center_x": -25.0, "center_z": -20.0, "radius": 50.0, "count": 40, "min_scale": 0.8, "max_scale": 1.8}, "Trees: Pine_1 x40"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pine_2.fbx", "center_x": 30.0, "center_z": -15.0, "radius": 40.0, "count": 25, "min_scale": 0.8, "max_scale": 1.6}, "Trees: Pine_2 x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pine_3.fbx", "center_x": -40.0, "center_z": 0.0, "radius": 30.0, "count": 15, "min_scale": 0.8, "max_scale": 1.5}, "Trees: Pine_3 x15"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/TwistedTree_1.fbx", "center_x": -15.0, "center_z": 30.0, "radius": 35.0, "count": 20, "min_scale": 1.0, "max_scale": 2.0}, "Trees: TwistedTree_1 x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/TwistedTree_2.fbx", "center_x": 10.0, "center_z": 40.0, "radius": 25.0, "count": 12, "min_scale": 0.8, "max_scale": 1.5}, "Trees: TwistedTree_2 x12"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/DeadTree_1.fbx", "center_x": 25.0, "center_z": 25.0, "radius": 30.0, "count": 12, "min_scale": 1.0, "max_scale": 1.8}, "Trees: DeadTree_1 x12"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/DeadTree_2.fbx", "center_x": -10.0, "center_z": -40.0, "radius": 20.0, "count": 8, "min_scale": 0.8, "max_scale": 1.5}, "Trees: DeadTree_2 x8"])
	
	# PHASE 4: Rocks (140 — 2x density)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_1.fbx", "center_x": 35.0, "center_z": -30.0, "radius": 35.0, "count": 20, "min_scale": 0.5, "max_scale": 2.5}, "Rocks: Rock_Medium_1 x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_2.fbx", "center_x": -35.0, "center_z": 20.0, "radius": 30.0, "count": 18, "min_scale": 0.5, "max_scale": 2.0}, "Rocks: Rock_Medium_2 x18"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Rock_Medium_3.fbx", "center_x": 10.0, "center_z": 35.0, "radius": 25.0, "count": 15, "min_scale": 0.5, "max_scale": 1.8}, "Rocks: Rock_Medium_3 x15"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pebble_Round_1.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 58.0, "count": 35, "min_scale": 0.2, "max_scale": 0.8}, "Rocks: Pebble_Round_1 x35"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Pebble_Round_2.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 58.0, "count": 35, "min_scale": 0.2, "max_scale": 0.8}, "Rocks: Pebble_Round_2 x35"])
	
	# PHASE 5: Ground cover (136 — 2x density)
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Bush_Common.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 58.0, "count": 45, "min_scale": 0.5, "max_scale": 1.5}, "Bushes x45"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Fern_1.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 58.0, "count": 35, "min_scale": 0.4, "max_scale": 1.0}, "Ferns x35"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Flower_4_Group.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 25, "min_scale": 0.4, "max_scale": 0.8}, "Flowers x25"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Mushroom_Laetiporus.fbx", "center_x": -20.0, "center_z": 20.0, "radius": 30.0, "count": 15, "min_scale": 0.3, "max_scale": 0.8}, "Mushrooms x15"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Grass_Common_Tall.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 20, "min_scale": 0.5, "max_scale": 1.2}, "Tall Grass x20"])
	s.append(["scatter_on_terrain", {"model_path": "res://models/nature/FBX/Grass_Common_Short.fbx", "center_x": 0.0, "center_z": 0.0, "radius": 55.0, "count": 20, "min_scale": 0.4, "max_scale": 0.8}, "Short Grass x20"])
	
	# PHASE 6: Monuments — Industrial complex (north-east) — ALL buildings
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-a.glb", "x": 40.0, "z": -40.0, "scale": 1.2}, "Building A"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-b.glb", "x": 45.0, "z": -35.0, "scale": 1.0}, "Building B"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-d.glb", "x": 50.0, "z": -42.0, "scale": 0.9}, "Building D"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-e.glb", "x": 35.0, "z": -35.0, "scale": 0.8}, "Building E"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/chimney-large.glb", "x": 52.0, "z": -38.0, "scale": 1.0}, "Chimney Large"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/chimney-medium.glb", "x": 38.0, "z": -42.0, "scale": 0.8}, "Chimney Medium"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/detail-tank.glb", "x": 48.0, "z": -38.0, "scale": 0.6}, "Detail Tank"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": 42.0, "z": -38.0, "scale": 0.5}, "Crate 1"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": 44.0, "z": -36.0, "scale": 0.5}, "Crate 2"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": 46.0, "z": -40.0, "scale": 0.4}, "Crate 3"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": 33.0, "z": -40.0, "scale": 0.7}, "Fence 1"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": 55.0, "z": -40.0, "scale": 0.7}, "Fence 2"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": 40.0, "z": -30.0, "scale": 0.7}, "Fence 3"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Stairs_Exterior_Sides.fbx", "x": 43.0, "z": -33.0, "scale": 0.6}, "Stairs 1"])
	
	# PHASE 6b: Gas Station / Outpost cluster (south-west)
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/building-c.glb", "x": -40.0, "z": 30.0, "scale": 1.0}, "Building C"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/chimney-small.glb", "x": -42.0, "z": 28.0, "scale": 0.6}, "Chimney Small"])
	s.append(["spawn_model", {"model_path": "res://models/industrial/Models/GLB/detail-tank.glb", "x": -36.0, "z": 33.0, "scale": 0.5}, "Detail Tank 2"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": -38.0, "z": 32.0, "scale": 0.5}, "Crate 4"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_Crate.fbx", "x": -44.0, "z": 32.0, "scale": 0.4}, "Crate 5"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Prop_WoodenFence_Single.fbx", "x": -35.0, "z": 28.0, "scale": 0.7}, "Fence 4"])
	s.append(["spawn_model", {"model_path": "res://models/medieval/FBX/Stairs_Exterior_Sides.fbx", "x": -43.0, "z": 35.0, "scale": 0.5}, "Stairs 2"])
	
	# PHASE 7: Roads — wider, more connected
	s.append(["spawn_box", {"x": 0.0, "z": -5.0, "w": 80.0, "h": 0.2, "d": 5.0, "color": "#3a3a3a"}, "Road 1 (main east-west)"])
	s.append(["spawn_box", {"x": 25.0, "z": -22.0, "w": 5.0, "h": 0.2, "d": 35.0, "color": "#3a3a3a"}, "Road 2 (to industrial)"])
	s.append(["spawn_box", {"x": -25.0, "z": 12.0, "w": 5.0, "h": 0.2, "d": 35.0, "color": "#3a3a3a"}, "Road 3 (to gas station)"])
	s.append(["spawn_box", {"x": 0.0, "z": 20.0, "w": 5.0, "h": 0.2, "d": 30.0, "color": "#2a2a2a"}, "Road 4 (south branch)"])
	s.append(["spawn_box", {"x": -10.0, "z": 0.0, "w": 30.0, "h": 0.2, "d": 5.0, "color": "#2a2a2a"}, "Road 5 (west connector)"])
	
	# PHASE 8: Lights — more, warmer
	s.append(["spawn_light", {"x": 0.0, "z": 0.0, "energy": 12.0, "range": 50.0, "color": "white"}, "Light center"])
	s.append(["spawn_light", {"x": 42.0, "z": -38.0, "energy": 6.0, "range": 25.0, "color": "warmyellow"}, "Light industrial 1"])
	s.append(["spawn_light", {"x": 48.0, "z": -40.0, "energy": 4.0, "range": 18.0, "color": "orange"}, "Light industrial 2"])
	s.append(["spawn_light", {"x": -40.0, "z": 30.0, "energy": 5.0, "range": 20.0, "color": "orange"}, "Light gas station"])
	s.append(["spawn_light", {"x": 20.0, "z": 20.0, "energy": 3.0, "range": 15.0, "color": "orange"}, "Light forest edge 1"])
	s.append(["spawn_light", {"x": -20.0, "z": -20.0, "energy": 3.0, "range": 15.0, "color": "orange"}, "Light forest edge 2"])
	s.append(["spawn_light", {"x": 0.0, "z": 35.0, "energy": 3.0, "range": 15.0, "color": "warmyellow"}, "Light south road"])
	s.append(["spawn_light", {"x": 30.0, "z": -20.0, "energy": 2.0, "range": 12.0, "color": "cyan"}, "Light lake 1"])
	s.append(["spawn_light", {"x": -30.0, "z": 25.0, "energy": 2.0, "range": 12.0, "color": "cyan"}, "Light lake 2"])
	
	# PHASE 9: PBR Materials — all objects
	s.append(["apply_material", {"model_name": "CommonTree", "texture_set": "grass"}, "PBR: CommonTrees"])
	s.append(["apply_material", {"model_name": "Pine", "texture_set": "grass"}, "PBR: Pines"])
	s.append(["apply_material", {"model_name": "TwistedTree", "texture_set": "grass"}, "PBR: TwistedTrees"])
	s.append(["apply_material", {"model_name": "DeadTree", "texture_set": "ground_dirt"}, "PBR: DeadTrees"])
	s.append(["apply_material", {"model_name": "Rock", "texture_set": "rock"}, "PBR: Rocks"])
	s.append(["apply_material", {"model_name": "Pebble", "texture_set": "rock"}, "PBR: Pebbles"])
	s.append(["apply_material", {"model_name": "Bush", "texture_set": "grass"}, "PBR: Bushes"])
	s.append(["apply_material", {"model_name": "Fern", "texture_set": "grass"}, "PBR: Ferns"])
	s.append(["apply_material", {"model_name": "Flower", "texture_set": "grass"}, "PBR: Flowers"])
	s.append(["apply_material", {"model_name": "Mushroom", "texture_set": "ground_dirt"}, "PBR: Mushrooms"])
	s.append(["apply_material", {"model_name": "Grass", "texture_set": "grass"}, "PBR: Grass models"])
	s.append(["apply_material", {"model_name": "building", "texture_set": "rock"}, "PBR: Buildings"])
	s.append(["apply_material", {"model_name": "chimney", "texture_set": "rock"}, "PBR: Chimneys"])
	s.append(["apply_material", {"model_name": "detail", "texture_set": "rock"}, "PBR: Detail tanks"])
	s.append(["apply_material", {"model_name": "Crate", "texture_set": "ground_dirt"}, "PBR: Crates"])
	s.append(["apply_material", {"model_name": "Fence", "texture_set": "ground_dirt"}, "PBR: Fences"])
	s.append(["apply_material", {"model_name": "Stairs", "texture_set": "ground_dirt"}, "PBR: Stairs"])
	
	return s
