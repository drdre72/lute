# Brick Wall Construction — Current Issues & Workflow

## What We're Building

Individual brick GameObjects laid one at a time by 3 autonomous builder NPCs
to construct a medieval village wall perimeter. Each brick is placed with a
0.5s timer and a LAY animation (crouch down, place, stand up).

## Current Brick Dimensions

- **Size**: 0.19m long x 0.1m thick x 0.04m tall (realistic brick)
- **Mortar gap**: ~1cm between bricks
- **Wall thickness**: 1 brick (0.1m) — no depth stacking
- **Pattern**: Running bond (half-brick offset every other row)
- **Pieces per 10m x 5m wall segment**: ~5000 bricks
- **Build pace**: 0.5s per brick with LAY animation
- **3 builders**: ~14 min per wall segment

## Architecture

### SpawnBox (VillageBuilder.cs)
- Uses `ModelRenderer` with `models/dev/box.vmdl` + `MaterialOverride`
- `go.WorldScale = size` scales the 1x1x1 box to brick dimensions
- `BoxCollider` added for collision
- Material: `materials/medieval/brick_wall.vmat` (references stone textures
  with `g_vTexCoordScale [10.000 10.000]`)

### BuildWallSegment (VillageBuilder.cs)
- Calculates bricks per row, rows, total bricks
- Loops row by row, placing each brick with `SpawnBox`
- Running bond: offset every other row by half a brick
- `await Task.DelaySeconds(BuildInterval)` between bricks (0.5s)
- `MaybeSave()` called after each brick for periodic saves

### Builder NPCs (NPCSpawner.cs)
- 3 builder NPCs with citizen bodies, PlayerController, NavMeshAgent
- Each has an "Eyes" child with CameraComponent for first-person vision
- `FreshBuild = true` — always fresh, no instant reconstruction from save
- `BuildInterval = 0.5f` — 0.5s per brick
- `WallMaterial = "materials/medieval/brick_wall.vmat"`

### LAY Animation (VillageBuilderController.cs)
- `PlayLayAnimation()` cycles citizen `duck` parameter
- Sin wave: 0 (standing) -> 1 (crouched) -> 0 over 0.5s cycle
- Matches the build pace so each brick has one crouch-and-rise gesture

## Current Issues

### 1. Walls appear as large panels, not individual bricks
**Status**: UNRESOLVED

The code correctly places individual brick-sized GameObjects (confirmed via
MCP: scale 7.48 x 3.94 x 1.57 inches = 0.19m x 0.1m x 0.04m). 344 pieces
confirmed for one wall segment. No large-scale panels found in inspection.

However, the user reports seeing "aggressively large panels" in the editor.
Possible causes:
- Bricks are very small (0.19m) and from a distance look like a solid wall
- Old walls from previous runs may still be in the scene (FreshBuild clears
  the save file but not runtime objects from previous play sessions)
- The brick_wall.vmat texture may make individual bricks hard to distinguish
- Camera angle/distance may make individual bricks appear as a texture

### 2. Builder NPC eyes cameras show wrong position
**Status**: PARTIALLY WORKING

Each builder NPC has an "Eyes" child with CameraComponent. The cameras are
created and detectable via MCP (4 Eyes objects found). However:
- Some cameras show the NPC's own face (camera inside the head model)
- Position may not correctly follow the parent NPC after warping
- The `LocalPosition = (0, 0, 64)` should put the camera at eye height but
  may need adjustment (move forward to avoid being inside the head)

### 3. Material shows as stone_wall instead of brick_wall
**Status**: INVESTIGATED

`Material.Load("materials/medieval/brick_wall.vmat")` succeeds (no warning
logged) but MCP reports `MaterialOverride: stone_wall.vmat`. This is likely
engine material deduplication — brick_wall.vmat and stone_wall.vmat reference
the same stone textures, so the engine may report the first-loaded material
name. The actual UV scale differs (10 vs 30) so the visual result should
differ. The brick_wall.vmat was force-compiled successfully.

### 4. EstimateTotalPieces is stale
**Status**: MINOR

`EstimateTotalPieces()` still uses hardcoded `"wall" => 12` from the old
large-panel approach. Should be updated to estimate ~5000 bricks per wall
segment. This only affects the log message, not actual construction.

## What We Tried

### Approach 1: PolygonMesh with custom UVs
- Created PolygonMesh with 8 vertices, 6 faces
- Assigned material via `AssignMaterialToFaces`
- Set UVs via `SetFaceTextureCoords` after material assignment
- **Result**: Walls rendered flat/untextured. UVs didn't work with PolygonMesh.
- **Reverted**: Switched to ModelRenderer + box.vmdl

### Approach 2: ModelRenderer + box.vmdl + MaterialOverride
- Used `models/dev/box.vmdl` (1x1x1 cube) with `WorldScale = size`
- `MaterialOverride = Material.Load(materialPath)`
- **Result**: Works! Textures show correctly. This is the proven approach
  the market walls use.
- **Adopted**: Current approach

### Approach 3: Large blocks (1m x 3m x 0.5m)
- Individual GameObjects but very large
- **Result**: Looked like large panels/slabs, not bricks
- **User feedback**: "still wall panels"

### Approach 4: Large blocks with mortar gaps (0.95m x 3m x 0.45m)
- Added 5cm mortar gaps between blocks
- **Result**: GPT-5 confirmed "individual brick blocks with dark gaps,
  running bond pattern with staggered vertical joints"
- **User feedback**: "definitely just generating aggressively large panels"

### Approach 5: Realistic small bricks (0.19m x 0.1m x 0.04m)
- Wall is 1 brick thick (no depth stacking)
- ~5000 bricks per wall segment
- **Result**: MCP confirms 344 pieces at correct scale (7.48 x 3.94 x 1.57 in)
- **User feedback**: "its definitely just generating aggressively large panels"
- **Status**: UNRESOLVED — code is correct but visual result doesn't match

### Approach 6: FreshBuild = true
- Force `FreshBuild = true` in NPCSpawner to prevent instant reconstruction
- **Result**: Prevents save-based reconstruction but old runtime objects from
  previous play sessions may still persist in the scene

## Verification Workflow

### Build
```powershell
cd C:\Users\Shadow\Documents\lute
dotnet build sbox/code/lute.csproj
```

### Sync to runtime addon
```powershell
$src = "C:\Users\Shadow\Documents\lute\sbox\code"
$dst = "C:\Users\Shadow\Documents\sbox-public-clean\game\addons\lute\code"
Copy-Item "$src\Building\VillageBuilder.cs" "$dst\Building\VillageBuilder.cs" -Force
Copy-Item "$src\Building\VillageBuilderController.cs" "$dst\Building\VillageBuilderController.cs" -Force
Copy-Item "$src\NPC\NPCSpawner.cs" "$dst\NPC\NPCSpawner.cs" -Force
Remove-Item "$dst\obj" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem "$dst" -Filter "*.cs" -Recurse | ForEach-Object { (Get-Item $_.FullName).LastWriteTime = Get-Date }
```

### Restart play mode
```powershell
# Via MCP
$body = @{jsonrpc="2.0"; method="tools/call"; params=@{name="play_stop"; arguments=@{}}} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:7269/mcp" -Method Post -Body $body -ContentType "application/json"
# Clear save
Remove-Item "C:\Users\Shadow\Documents\sbox-public-clean\game\data\local\lute#local\village_save.json" -Force -ErrorAction SilentlyContinue
# Start
$body2 = @{jsonrpc="2.0"; method="tools/call"; params=@{name="play_start"; arguments=@{}}} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:7269/mcp" -Method Post -Body $body2 -ContentType "application/json"
```

### Visual verification via GPT-5
```powershell
# Editor camera at village walls
python agent\gpt_eyes.py "Describe the walls" --village --save scrap\shot.png

# Builder NPC first-person camera
python agent\gpt_eyes.py "Describe what you see" --camera <eyes_id> --play
```

### MCP inspection
```powershell
# Find wall pieces
$body = @{jsonrpc="2.0"; method="tools/call"; params=@{name="find_game_objects"; arguments=@{name="Village_Wall"}}} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:7269/mcp" -Method Post -Body $body -ContentType "application/json"

# Check piece scale
$body = @{jsonrpc="2.0"; method="tools/call"; params=@{name="get_game_object"; arguments=@{id="<piece_id>"; includeComponentProperties=$true}}} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:7269/mcp" -Method Post -Body $body -ContentType "application/json"
```

### Log verification
```powershell
$log = "C:\Users\Shadow\Documents\sbox-public-clean\game\logs\sbox-dev.log"
Select-String -Path $log -Pattern "VillageBuilder saved" | Select-Object -Last 3
Select-String -Path $log -Pattern "SpawnBox material.*failed" | Select-Object -Last 3
```

## Key Files

- `sbox/code/Building/VillageBuilder.cs` — wall construction, SpawnBox, BuildWallSegment
- `sbox/code/Building/VillageBuilderController.cs` — NPC controller, LAY animation
- `sbox/code/NPC/NPCSpawner.cs` — NPC spawning, FreshBuild, BuildInterval, Eyes camera
- `sbox/Assets/materials/medieval/brick_wall.vmat` — brick material (references stone textures)
- `agent/gpt_eyes.py` — GPT-5 vision bridge with --village and --camera options
