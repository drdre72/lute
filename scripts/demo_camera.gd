extends Camera3D

var cameras: Array[Camera3D] = []
var current_index: int = 0
var switch_timer: float = 0.0
var switch_interval: float = 8.0

func _ready():
	# Find all cameras in the scene
	_find_cameras(get_tree().root)
	
	# Make this camera current
	current = true
	
	# If we found other cameras, start cycling
	if cameras.size() > 0:
		print("DemoMode: Found ", cameras.size(), " cameras. Cycling every ", switch_interval, "s")

func _find_cameras(node: Node):
	if node is Camera3D and node != self:
		cameras.append(node)
	for child in node.get_children():
		_find_cameras(child)

func _process(delta):
	if cameras.size() == 0:
		return
	
	switch_timer += delta
	if switch_timer >= switch_interval:
		switch_timer = 0.0
		current_index = (current_index + 1) % cameras.size()
		var cam = cameras[current_index]
		# Copy transform from target camera
		global_transform = cam.global_transform
		fov = cam.fov
		print("DemoMode: Switched to camera ", current_index, " - ", cam.name)

func _input(event):
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_SPACE:
			# Manual switch on spacebar
			switch_timer = 0.0
			current_index = (current_index + 1) % cameras.size()
			var cam = cameras[current_index]
			global_transform = cam.global_transform
			fov = cam.fov
			print("DemoMode: Manual switch to ", cam.name)
		elif event.keycode == KEY_ESCAPE:
			get_tree().quit()
