@tool
extends VBoxContainer

var plugin # AI Pipeline EditorPlugin instance, set via set_plugin()
var server # AIPipelineServer instance, set directly by plugin.gd

@onready var status_label: Label = $StatusRow/StatusLabel
@onready var port_spin: SpinBox = $StatusRow/PortSpin
@onready var toggle_button: Button = $StatusRow/ToggleButton
@onready var clear_log_button: Button = $StatusRow/ClearLogButton
@onready var log_edit: TextEdit = $LogEdit


func _ready() -> void:
	toggle_button.pressed.connect(_on_toggle_pressed)
	clear_log_button.pressed.connect(_on_clear_log_pressed)
	_refresh_status()


func set_plugin(p) -> void:
	plugin = p


func _on_toggle_pressed() -> void:
	if server == null:
		return
	if server.running:
		server.stop()
		append_log("Server stopped.")
	else:
		var ok: bool = server.start(int(port_spin.value))
		if ok:
			append_log("Server started on port %d." % server.port)
		else:
			append_log("Failed to start server on port %d." % int(port_spin.value))
	_refresh_status()


func _on_clear_log_pressed() -> void:
	log_edit.text = ""


func _refresh_status() -> void:
	if server == null:
		status_label.text = "Server: unavailable"
		return
	if server.running:
		status_label.text = "Server: running on port %d" % server.port
		toggle_button.text = "Stop Server"
		port_spin.editable = false
	else:
		status_label.text = "Server: stopped"
		toggle_button.text = "Start Server"
		port_spin.editable = true


func append_log(text: String) -> void:
	if log_edit == null:
		return
	log_edit.text += text + "\n"
	log_edit.scroll_vertical = log_edit.get_line_count()


func _process(_delta: float) -> void:
	_refresh_status()
