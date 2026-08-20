extends Camera3D

## Smooth-follow camera that tracks a target node.

@export var target_path: NodePath = "../LLMAgent"
@export var follow_speed: float = 3.0
@export var offset: Vector3 = Vector3(0, 8, 12)

var _target: Node3D
var _current_pos: Vector3

func _ready() -> void:
	_current_pos = global_position

func _process(delta: float) -> void:
	if _target == null or not is_instance_valid(_target):
		if target_path != "":
			_target = get_node_or_null(target_path)
		if _target == null:
			return
	
	var target_pos = _target.global_position + offset
	_current_pos = _current_pos.lerp(target_pos, follow_speed * delta)
	global_position = _current_pos
	
	# Look at target position (slightly above ground)
	var look_target = _target.global_position + Vector3(0, 1, 0)
	if global_position.distance_to(look_target) > 0.1:
		look_at(look_target)
