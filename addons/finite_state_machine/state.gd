## Base class for State.
##
## Extend it with your own lifecycle funcs (enter, update, physics_update, exit).
##
## Use transition(to: String) to trigger state switches.
@icon('state_icon.svg')
class_name State
extends FSM

@export_storage var _finite_state_machine: FiniteStateMachine
@export_storage var id: String

func enter():
	pass

@warning_ignore('unused_parameter')
func update(delta: float):
	pass

@warning_ignore('unused_parameter')
func physics_update(delta: float):
	pass

func exit():
	pass

## Transition to the target State. Only the current state or parent state can execute transitions unless [force] is enabled [br]
func transition(to_id: String, force: bool = false):
	var is_current_self_or_child: bool = _finite_state_machine.current_state == self or _finite_state_machine.current_state.id.begins_with(self.id)
	if _finite_state_machine and is_current_self_or_child:
		_finite_state_machine.transition(to_id)
