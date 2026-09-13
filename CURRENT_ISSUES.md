# Current Issues — Lute Village Builder

## Build State (as of 2026-09-12 20:04)

The Lute addon compiles and loads correctly. The village builder starts
with 146 tasks and all 8 corner butt flags are correctly assigned:

```
Wall_N_0:  CornerButtCourses=2, CornerButtSide=1 (odd, left)
Wall_N_16: CornerButtCourses=2, CornerButtSide=2 (odd, right)
Wall_S_0:  CornerButtCourses=2, CornerButtSide=1 (odd, left)
Wall_S_16: CornerButtCourses=2, CornerButtSide=2 (odd, right)
Wall_E_0:  CornerButtCourses=1, CornerButtSide=1 (even, left)
Wall_E_16: CornerButtCourses=1, CornerButtSide=2 (even, right)
Wall_W_0:  CornerButtCourses=1, CornerButtSide=1 (even, left)
Wall_W_16: CornerButtCourses=1, CornerButtSide=2 (even, right)
```

The half-width fix (17.5m → 17m) resolved the missing right-segment
corner butt flags. All 4 corners should now have correct alternating
ownership.

## Open Issues

### 1. Editor crashes when starting play mode via MCP

**Status:** Unresolved

The S&Box editor crashes when `play_start` is called via MCP after the
editor restarts. The crash happens after the Lute addon loads and the
village builder initializes (logs show successful initialization with
all corner butt flags). The MCP server becomes unreachable
(connection refused).

**Likely cause:** The ghost-placement validation in
`ConstructionDirector.ClaimNextTask` calls
`ReservationManager.ValidatePlacement`, which calls
`CheckSceneGeometryCollision`, which uses `Scene.Trace.Ray`. This may
crash if the scene reference is stale or if raycasting is called before
the scene is fully initialized.

**Workaround:** The `CheckSceneGeometryCollision` method already has a
null check for `_scene`, but the scene reference may be set to a stale
scene object that has been disposed. Need to verify the scene reference
is valid before raycasting.

### 2. MCP spatial tools not registered

**Status:** Unresolved

The `lute_spatial` MCP tools are not registered. Two approaches were
tried:

1. **Direct MCP attributes in Lute addon** — failed because the Lute
   addon can't reference `Sandbox.Tools.dll` (the editor's addon
   compiler doesn't support manual assembly references to engine
   tools).

2. **Reflection wrapper in tools addon** — the `LuteSpatialMcp.cs` file
   was placed in the tools addon, but the editor crashed on startup.
   The file has been removed from the tools addon.

**Current state:** The `LuteSpatialApi` static class exists in the Lute
addon (`Lute.Building` namespace) with all the spatial query methods,
but they're not exposed as MCP tools. The methods can be called from
Python scripts via a custom console command or other mechanism.

**Next step:** Add a simple console command in the Lute addon that calls
the `LuteSpatialApi` methods and prints results, so Python scripts can
invoke it via the MCP `execute_console_command` tool (if available) or
similar.

### 3. Corner topology verification incomplete

**Status:** In progress

The SW corner (Wall_W_0 <-> Wall_S_0) has been verified multiple times
with the topology probe and GPT vision — it passes with clean
alternating ownership. The other 3 corners (SE, NW, NE) have not yet
been verified because the build hasn't completed enough wall segments.

With the half-width fix, all 8 corner segments now have correct butt
flags. Once the editor crash is resolved, the build can proceed and all
4 corners can be verified.

### 4. Build pacing

The build estimate is ~14.5 hours at 0.5s/piece. The `BuildInterval` can
be reduced to 0.01f via MCP to speed up construction, but the editor
crash prevents this from being set after restart.

## Commits Pushed

```
6bb8428 Fix WallOuterHalfWidth to be exact multiple of segment length
103eaee Add OBB struct and ghost-placement validation to ReservationManager
dea49ea Wire ghost-placement validation into ConstructionDirector.ClaimNextTask
2fec3de Add Lute MCP spatial query tools
07bbd48 Fix csproj: add Sandbox.Tools reference and suppress CA2000/CA2007 warnings
3226ce3 Move MCP tools to plain static class, add reflection wrapper in tools addon
```

## Files Changed

- `sbox/code/Building/OBB.cs` (new) — OBB struct with SAT overlap detection
- `sbox/code/Building/ReservationManager.cs` — ghost-placement validation, OBB support
- `sbox/code/Building/ConstructionDirector.cs` — wired ValidatePlacement into ClaimNextTask
- `sbox/code/Building/LuteSpatialTools.cs` (new) — LuteSpatialApi static class
- `sbox/code/Building/VillageGrammar.cs` — WallOuterHalfWidth 17.5m → 17m, butt flag logging
- `sbox/code/Building/VillageBuilder.cs` — SetScene call for raycast-based checks
- `sbox/code/lute.csproj` — removed Sandbox.Tools.dll reference, suppressed CA2000/CA2007
