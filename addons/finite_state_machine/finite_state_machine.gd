## The states manager.
@tool
@icon("fsm_icon.svg")
class_name FiniteStateMachine
extends FSM

@export var initial_state: State

## Disable the machine, a very handy option during development.
@export var disabled: bool:
	set(value):
		disabled = value
		self.process_mode = Node.PROCESS_MODE_DISABLED if disabled else PROCESS_MODE_INHERIT

var current_state: State
var _states: Dictionary[String, State]
var is_editor: bool = Engine.is_editor_hint()

## Notify when current state transitioned (from->to)
signal transitioned(from: State, to: State)

func _get_configuration_warnings() -> PackedStringArray:
	var warnings = PackedStringArray()
	
	if get_children().is_empty():
		warnings.append('Please add some "State" nodes, and extend the script to define it\'s lifecycle behavior')
	
	var stack = [['', self]] # [[parent_id, node]]
	while not stack.is_empty():
		var pair = stack.pop_front()
		var parent_id: String = pair[0]
		var node: Node = pair[1]
		var id = parent_id.path_join(node.name) if node != self else ''
		
		if node != self:
			if node is State:
				if node.get_script() == State:
					warnings.append('%s: right click and select "Extend Script" to extend the state behavior' % id)
			else:
				warnings.append('%s: should be a "State" node instead of "%s" node' % [id, node.get_class()])
		
		for child in node.get_children():
			if child is State:
				stack.append([id, child])
			
	return warnings

func _enter_tree() -> void:
	if is_editor: return
	var stack = [['', self]] # [[parent_id, node]]
	while not stack.is_empty():
		var pair = stack.pop_back()
		var parent_id: String = pair[0]
		var node: Node = pair[1]
		var id = parent_id.path_join(node.name) if node != self else ''
		
		if node != self:
			if node is State:
				_states[id] = node
				node._finite_state_machine = self
				node.id = id
		
		for child in node.get_children():
			if child is State:
				stack.append([id, child])

func _ready() -> void:
	if disabled: return
	#enter at first
	current_state = initial_state
	if current_state and not is_editor:
		_state_down_call('', current_state.id, 'enter')

func _state_down_call(from_state_id: String, to_state_id: String, method_name: String, ...args: Array):
	if is_editor: return
	var parts = to_state_id.split('/')
	var current_state_id = ''
	for p in parts:
		current_state_id = current_state_id.path_join(p)
		if from_state_id.begins_with(current_state_id):
			continue
		var state = _states[current_state_id]
		if state:
			state.callv(method_name, args)

func _state_up_call(from_state_id: String, to_state_id: String, method_name: String, ...args: Array):
	if is_editor: return
	var current_state_id = from_state_id
	while not current_state_id.is_empty() and current_state_id != to_state_id.get_base_dir():
		var state = _states[current_state_id]
		if state and state is State:
			state.callv(method_name, args)
		current_state_id = current_state_id.get_base_dir()

func transition(to_id: String):
	if disabled: return
	var to_state: State = _states[to_id]
	if to_state and to_state != current_state:
		var from_state = current_state
		_state_up_call(from_state.id, to_state.id, 'exit')
		_state_down_call(from_state.id, to_state.id, 'enter')
		transitioned.emit(from_state, to_state)
		current_state = to_state

func _process(delta: float) -> void:
	if is_editor: return
	if current_state:
		_state_down_call('', current_state.id, 'update', delta)
		
func _physics_process(delta: float) -> void:
	if is_editor: return
	if current_state:
		_state_down_call('', current_state.id, 'physics_update', delta)
