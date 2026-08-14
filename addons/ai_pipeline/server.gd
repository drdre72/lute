extends RefCounted

# Minimal HTTP/JSON server used by the AI Pipeline editor plugin.
# Listens on a TCP port and accepts POST /rpc requests with a JSON body of
# the form {"tool": "<name>", "args": {...}}, dispatching to `plugin.handle_rpc`.

var tcp_server := TCPServer.new()
var port: int = 6400
var running: bool = false
var plugin = null # set by plugin.gd; expected to implement handle_rpc(Dictionary) -> Dictionary

var _connections: Array = []


func start(p_port: int = 6400) -> bool:
	if running:
		stop()
	port = p_port
	var err := tcp_server.listen(port)
	if err != OK:
		running = false
		return false
	running = true
	return true


func stop() -> void:
	running = false
	tcp_server.stop()
	for conn in _connections:
		var peer: StreamPeerTCP = conn["peer"]
		peer.disconnect_from_host()
	_connections.clear()


func poll() -> void:
	if not running:
		return

	while tcp_server.is_connection_available():
		var peer := tcp_server.take_connection()
		_connections.append({
			"peer": peer,
			"buffer": PackedByteArray(),
			"headers_done": false,
			"header_end": -1,
			"content_length": 0,
			"header_text": "",
		})

	var still_open: Array = []
	for conn in _connections:
		var peer: StreamPeerTCP = conn["peer"]
		peer.poll()
		if peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
			continue

		var avail := peer.get_available_bytes()
		if avail > 0:
			var chunk = peer.get_data(avail)
			if chunk[0] == OK:
				conn["buffer"].append_array(chunk[1])

		if not conn["headers_done"]:
			var idx := _find_header_end(conn["buffer"])
			if idx != -1:
				conn["headers_done"] = true
				conn["header_end"] = idx
				conn["header_text"] = conn["buffer"].slice(0, idx).get_string_from_utf8()
				conn["content_length"] = _parse_content_length(conn["header_text"])

		var request_complete := false
		if conn["headers_done"]:
			var body_start: int = conn["header_end"] + 4
			var body_len: int = conn["buffer"].size() - body_start
			if body_len >= conn["content_length"]:
				request_complete = true

		if request_complete:
			var body_start: int = conn["header_end"] + 4
			var body_bytes: PackedByteArray = conn["buffer"].slice(body_start, body_start + conn["content_length"])
			_handle_request(peer, conn["header_text"], body_bytes.get_string_from_utf8())
		else:
			still_open.append(conn)

	_connections = still_open


func _find_header_end(buffer: PackedByteArray) -> int:
	var n := buffer.size()
	if n < 4:
		return -1
	for i in range(n - 3):
		if buffer[i] == 13 and buffer[i + 1] == 10 and buffer[i + 2] == 13 and buffer[i + 3] == 10:
			return i
	return -1


func _parse_content_length(header_text: String) -> int:
	for line in header_text.split("\r\n"):
		if line.to_lower().begins_with("content-length:"):
			var parts := line.split(":", true, 1)
			if parts.size() == 2:
				return parts[1].strip_edges().to_int()
	return 0


func _handle_request(peer: StreamPeerTCP, header_text: String, body_text: String) -> void:
	var lines := header_text.split("\r\n")
	var request_line: String = lines[0] if lines.size() > 0 else ""
	var parts := request_line.split(" ")
	var method: String = parts[0] if parts.size() > 0 else ""
	var path: String = parts[1] if parts.size() > 1 else ""

	var status := "404 Not Found"
	var response_body := "{\"error\":\"not found\"}"

	if path == "/ping":
		status = "200 OK"
		response_body = "{\"ok\":true}"
	elif path == "/rpc" and method == "POST":
		var json := JSON.new()
		var parse_err := json.parse(body_text)
		if parse_err == OK and typeof(json.data) == TYPE_DICTIONARY:
			var result: Dictionary = {"error": "no plugin attached"}
			if plugin != null and plugin.has_method("handle_rpc"):
				result = plugin.handle_rpc(json.data)
			status = "200 OK"
			response_body = JSON.stringify(result)
		else:
			status = "400 Bad Request"
			response_body = "{\"error\":\"invalid json body\"}"

	var body_bytes := response_body.to_utf8_buffer()
	var header := "HTTP/1.1 %s\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n" % [status, body_bytes.size()]
	peer.put_data(header.to_utf8_buffer())
	peer.put_data(body_bytes)
	peer.disconnect_from_host()
