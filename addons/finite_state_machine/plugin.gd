@tool
extends EditorPlugin


func _enable_plugin() -> void:
	# Add autoloads here.
	pass


func _disable_plugin() -> void:
	# Remove autoloads here.
	pass


func _enter_tree() -> void:
	# Initialization of the plugin goes here.

	# Check if script template folder exists, if not create it.
	
	if !DirAccess.dir_exists_absolute("res://script_templates"):
		DirAccess.make_dir_absolute("res://script_templates")
		move_folder_contents("res://addons/finite_state_machine/script_templates", "res://script_templates")
	else:
		move_folder_contents("res://addons/finite_state_machine/script_templates", "res://script_templates")
	
	pass


func _exit_tree() -> void:
	# Clean-up of the plugin goes here.
	pass




func move_folder_contents(source_dir: String, target_dir: String):
	var dir = DirAccess.open(source_dir)
	
	if dir:
		# Ensure target directory exists before moving files
		if not DirAccess.dir_exists_absolute(target_dir):
			DirAccess.make_dir_recursive_absolute(target_dir)
			
		dir.list_dir_begin()
		var file_name = dir.get_next()
		
		while file_name != "":
			# Ignore directory navigation pointers
			if file_name != "." and file_name != "..":
				if dir.current_is_dir():
					# Recursively handle subfolders if they exist
					var sub_source = source_dir.path_join(file_name)
					var sub_target = target_dir.path_join(file_name)
					move_folder_contents(sub_source, sub_target)
				else:
					# Safely map out old and new file paths
					var old_path = source_dir.path_join(file_name)
					var new_path = target_dir.path_join(file_name)
					
					# Move the file
					DirAccess.rename_absolute(old_path, new_path)
					
			file_name = dir.get_next()
		dir.list_dir_end()
	else:
		print("Failed to open source directory: ", source_dir)
