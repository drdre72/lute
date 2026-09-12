# Remaining Issues — Post PR #3 Merge

After merging `brick-wall-hardening` (PR #3), which fixed the 50x
`box.vmdl` scale mismatch. The following issues remain, ordered by
priority.

## 1. Running bond pattern not visible

**Status**: UNVERIFIED

GPT-5 reports "stacked/straight bond, not running bond" when viewing the
wall close-up. The code offsets every other row by half a brick
(`rowOffset = brickSpacingX * 0.5f`), but only a few courses may have
been built at inspection time.

**To verify**: Wait for 10+ courses to build, then screenshot again.
If still stacked, check that `row % 2` logic is correct and that the
offset is applied in the right axis relative to the wall's rotation.

**File**: `sbox/code/Building/VillageBuilder.cs` — `BuildWallSegment()`

## 2. Per-brick physics performance

**Status**: DEFERRED (by design)

Each brick has a `BoxCollider`. With ~5000 bricks per wall segment and
257 segments, that's ~1.3M colliders. This will cause severe physics
overhead.

**Agreed approach** (from user):
- Bricks should be **instanced as part of a growing wall segment** with
  builder animation, not as individual physics bodies.
- Hold physics unless placed as instanced wall segment piece / complete
  wall.
- Upon destruction/wall collapse, individual brick physics return, then
  clean up (later).

**Implementation needed**:
- Replace per-brick `BoxCollider` with a single segment-level collider
  (or no collider until segment is complete).
- Use instanced rendering or merged mesh for completed segments.
- Keep individual brick GameObjects only for the actively-building
  segment; merge into a static instanced mesh on completion.

**File**: `sbox/code/Building/VillageBuilder.cs` — `SpawnBox()`

## 3. Builder NPC eyes camera shows NPC's own face

**Status**: PARTIALLY FIXED

The "Eyes" child with `CameraComponent` is at `LocalPosition = (0, 0, 64)`
(eye height). But the camera renders the inside of the NPC's head model
because it's too close to the face mesh.

**Fix needed**: Move the eyes forward by ~10-20 units (0.25-0.5m) so the
camera is in front of the face, not inside it:
```csharp
eyesGo.LocalPosition = new Vector3( 0, 10f, 64f );  // forward + eye height
```

Also need to ensure the eyes follow the NPC's yaw rotation so the camera
looks where the NPC faces.

**File**: `sbox/code/NPC/NPCSpawner.cs` — `SpawnVillageCitizenBody()`

## 4. Material reports as stone_wall instead of brick_wall

**Status**: INVESTIGATED — likely engine deduplication

`Material.Load("materials/medieval/brick_wall.vmat")` succeeds (no
warning logged) but MCP `get_game_object` reports
`MaterialOverride: materials/medieval/stone_wall.vmat`.

`brick_wall.vmat` and `stone_wall.vmat` reference the same stone textures
(`stone_color.png`, `stone_normal.png`, `stone_rough.png`). The engine
likely deduplicates materials with identical texture references and
reports the first-loaded material name.

The only difference is `g_vTexCoordScale` (10 vs 30), which affects UV
tiling but not the material identity.

**Fix options**:
- Give `brick_wall.vmat` a distinct texture (actual brick texture) so
  the engine doesn't deduplicate.
- Or accept the deduplication — the visual result is correct, just the
  reported name is misleading.

**File**: `sbox/Assets/materials/medieval/brick_wall.vmat`

## 5. Brick texture compilation pipeline broken

**Status**: DEFERRED

The `brick_color.vtex`, `brick_normal.vtex`, `brick_rough.vtex` files
fail to compile with:
> `Error reading texture compile settings ... Expecting a .vtex file.`

Attempts made: UTF-8 BOM, CRLF, Python-style booleans, copying working
stone texture metadata. None worked.

**Workaround**: `brick_wall.vmat` references existing compiled stone
textures. This works but means the brick walls look like stone, not
brick.

**Fix needed**: Properly author `.vtex` files that the S&Box compiler
accepts, or import brick textures through the editor's texture import
tool.

**Files**:
- `sbox/Assets/materials/medieval/brick_color.vtex`
- `sbox/Assets/materials/medieval/brick_normal.vtex`
- `sbox/Assets/materials/medieval/brick_rough.vtex`

## 6. EstimateTotalPieces still stale in some paths

**Status**: MINOR

The `EstimateTotalPieces()` function was updated in PR #3 to use
corrected wall workloads (~5151 per segment). However, the log still
showed "Estimated ~3963 pieces" in one run — this may be from a stale
build or a path that wasn't updated.

**To verify**: Rebuild and check the log message after restart.

**File**: `sbox/code/Building/VillageBuilder.cs` — `EstimateTotalPieces()`

## 7. Old runtime objects from previous runs persist

**Status**: KNOWN LIMITATION

`FreshBuild = true` clears the save file but does not destroy runtime
objects from previous play sessions. When play mode stops and restarts,
old `MedievalVillage` GameObjects may persist in the scene.

**Fix needed**: On `OnStart()` with `FreshBuild = true`, destroy any
existing `MedievalVillage` root before creating a new one.

**File**: `sbox/code/Building/VillageBuilder.cs` — `OnStart()`

## 8. Camera forward offset for builder eyes

**Status**: DEFERRED (part of issue #3)

The builder eyes camera needs a forward offset so it doesn't render
inside the NPC's head. This is the same fix as issue #3 but listed
separately in the PR checklist as a deferred item.

## 9. Segment-level final collision/occupancy

**Status**: DEFERRED

Per PR #3 checklist: segment-level collision and occupancy validation
should be done when a wall segment completes, not per-brick. This is
related to issue #2 (per-brick physics performance).

## 10. task_199 / task_235 footprint problems

**Status**: DEFERRED

Per PR #3 checklist: specific tasks (task_199, task_235) have blueprint
footprint mismatches that cause placement failures. These need
individual investigation.
