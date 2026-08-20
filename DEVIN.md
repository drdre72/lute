# Lute - LLM Agent 3D World Builder

## Overview
Godot 4.7 project with an LLM-powered agent that builds 3D worlds autonomously using tool-calling.

## Tech Stack
- **Engine:** Godot 4.7 (Mono)
- **Language:** GDScript
- **LLM:** qwen2.5-coder-7b-instruct-mlx via LM Studio (OpenAI-compatible API at localhost:1234)
- **Admin server:** TCP on port 6401 for remote commands

## Key Files
- `scripts/llm_agent_3d.gd` — Main agent controller (movement, animations, LLM calls, camera follow, auto-save)
- `scripts/llm_tools_3d.gd` — Tool library (spawn, move, build, say, teleport, scale, rotate, etc.)
- `scripts/camera_follow.gd` — Unused (camera follow is now inline in llm_agent_3d.gd)
- `scenes/llm_agent_3d_test.tscn` — Main scene with agent, camera, light, ground
- `project.godot` — Godot project config
- `progress.txt` — Development progress log

## Running
1. Open `project.godot` in Godot 4.7
2. Start LM Studio with qwen2.5-coder-7b-instruct-mlx model on port 1234
3. Run the scene `scenes/llm_agent_3d_test.tscn`
4. Agent auto-builds world following checklist, responds to admin chat on port 6401

## Admin Commands
```bash
# Send message to agent
curl http://127.0.0.1:6401/admin/hello

# Or via Python
python3 -c "import socket; s=socket.socket(); s.connect(('127.0.0.1',6401)); s.sendall(b'GET /admin/hello HTTP/1.1\r\nHost: localhost\r\n\r\n'); print(s.recv(1024)); s.close()"
```
