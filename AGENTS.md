# AGENTS.md — Lute (S&Box) Agent Knowledge Cache

Persistent, hand-maintained notes for AI agents (Devin, GLM, etc.) working on
this repo. This exists because the coding model has **no native vision
layer** — it cannot see the S&Box editor viewport directly — and because the
S&Box C# API is new enough that models will hallucinate outdated Garry's Mod
/ early-S&Box hooks if they don't check the actual engine source first.
Read this before writing S&Box code so you don't re-discover the same APIs
every session.

## Project layout

- `PRD.md` — product/design spec (game design, not engine-specific). Read
  this first for *what* to build.
- `sbox/` — the S&Box game project (this repo's copy).
  - `sbox/code/` — all C# gameplay code, built via `sbox/code/lute.csproj`.
  - `sbox/Assets/scenes/` — `.scene` files (JSON, see "Scene file format" below).
  - `sbox/ProjectSettings/` — `Input.config`, `Collision.config`, `Platform.config`.
  - `sbox/.sbox/cloud/` — downloaded/cloud asset cache (materials, models, textures).
- `agent/sbox_verify.ps1` — the verification loop script (see below). Run
  this after every build/compile iteration instead of trying to "look at"
  the editor.
- `C:\Users\Shadow\Documents\sbox-public-clean\` — the S&Box **engine
  source** (not part of this repo). Search here when you need to know an
  exact API signature instead of guessing:
  - `engine/Sandbox.Engine/Scene/Components/` — built-in components (Terrain,
    lights, colliders, PlayerController, etc.)
  - `engine/Sandbox.System/Math/` — `Noise.cs`, `NoiseField.cs`, math utilities.
  - `engine/Tests/` — unit/integration tests are often the best usage examples
    (e.g. `TerrainComponentTest` shows the exact runtime pattern for writing
    heightmap data).
  - `game/addons/tools/Code/Scene/Terrain/Tools/` — editor terrain brush
    tools; good reference for how the *editor* mutates terrain at runtime
    (`SyncGPUTexture`, `UpdateCollision`, etc.) even though those tools
    themselves are editor-only.
  - `game/addons/lute/` — **the editor's live copy of this project**. S&Box
    installs/mounts game projects as addons inside its own install
    directory. If this repo's `sbox/` folder is ever lost, check here first
    — it survives independently of the repo folder. Keep both copies in
    sync manually (copy changed `.cs`/`.scene` files both directions) until
    a proper sync workflow exists.

A directory junction exists at `C:\Users\Shadow\Documents\bin` →
`sbox-public-clean\game\bin` so `dotnet build sbox/code/lute.csproj` resolves
its `../../../bin/managed/*.dll` references from the CLI (mirrors what the
S&Box editor does internally).

## Closing the vision gap: `agent/sbox_verify.ps1`

The coding model cannot process images (even though the `read` tool accepts
PNGs, they are not actually visible to it in this setup). Do **not** rely on
literally "looking at" a screenshot. Instead, run:

```powershell
powershell -File agent\sbox_verify.ps1
```

This produces a text report (also saved to `scrap/verify_<timestamp>.txt`)
with three independent signals, each cheap to reason about as plain text:

1. **Coarse screenshot pixel analysis** — a 5x5 color grid + brightness
   histogram of the primary screen. Useful for sanity checks: an all-black
   or all-(24,24,24) viewport means nothing rendered or the editor crashed;
   a warm brown/green blob in the center quadrant is probably terrain or
   world geometry; a uniform bright color across the whole grid suggests a
   shader/texture problem. This is a coarse proxy, not real vision — don't
   over-trust exact colors, use it to catch gross failures (black screen,
   viewport not visible, etc).
2. **Editor log tail** (`sbox-public-clean/game/logs/sbox-dev*.log`) — the
   most reliable signal. Every `LuteGame`/`LuteWorld`/`LuteTerrainGenerator`
   `Log.Info(...)` call shows up here with a timestamp, so you can confirm
   code actually ran (not just compiled). Lines matching
   `error|exception|fail` are auto-flagged at the bottom of the report.
3. **Scene file dump** — parses the active `.scene` JSON and prints every
   GameObject's name, world position, and component types, indented by
   hierarchy. This is ground truth for "where is everything", independent
   of whether it's rendering correctly — use it to catch objects spawned
   out of bounds, missing components, or wrong parenting.

Run this after every terrain/world-generation change, before declaring a
build "done". Compare `Lute: ...` log lines' timestamps to confirm your
latest code (not a stale build) actually executed.

### Known PowerShell/JSON gotcha

S&Box scene files use `"__type"` as the component type-discriminator key.
**`ConvertFrom-Json` silently drops this key**, and
`System.Web.Script.Serialization.JavaScriptSerializer` throws
(`Value cannot be null. Parameter name: type`) trying to parse it — both are
.NET JSON deserializer conventions where `__type`/`$type` is reserved for
polymorphic type hydration. Work around it by renaming the key before
parsing:

```powershell
$raw = (Get-Content $scenePath -Raw) -replace '"__type"', '"_ObjType"'
$json = $raw | ConvertFrom-Json
# now $component._ObjType works
```

`sbox_verify.ps1` already does this — copy the pattern if you write your own
scene-parsing script.

## S&Box API notes (from engine source, verified against this repo's usage)

### Component model

- Gameplay entities are `GameObject`s with `Component` subclasses attached.
- Lifecycle hooks: `OnStart()`, `OnUpdate()`, `OnFixedUpdate()`, `OnEnabled()`,
  `OnDisabled()`, `OnDestroy()`.
- `[Property]` exposes a field/property to the editor inspector.
- `[RequireComponent]` auto-adds/requires a sibling component (see
  `LutePlayer.Controller`).
- Networking: `[Sync]` on a property replicates it; `[Broadcast]` marks an
  RPC method. (Not yet used in this repo — `LutePlayer`/`LuteWorld` are
  currently single-player/local; add these when multiplayer lands per PRD §5.1.)
- `Scene.CreateObject(true)` creates a new GameObject in the active scene.
  `go.SetParent(parent)`, `go.WorldPosition`, `go.WorldRotation`,
  `go.WorldScale` control the transform.
- `Components.GetOrCreate<T>()` on a Component/GameObject fetches or adds a
  sibling component (used in `LuteGame.OnStart` and `LuteWorld.Build` to
  attach `LuteWorld`/`LuteTerrainGenerator`).

### Terrain (`Sandbox.Terrain` + `Sandbox.TerrainStorage`)

Source: `engine/Sandbox.Engine/Scene/Components/Terrain/Terrain*.cs`,
`engine/Sandbox.Engine/Resources/Terrain/TerrainStorage.cs`.

- `Terrain` is a `Collider` component (`IsConcave = true`). Attach with
  `go.AddComponent<Terrain>()`.
- `TerrainStorage` is a `GameResource` holding the actual heightmap/control
  map data. Create at runtime with `new TerrainStorage()` — it defaults to
  512 resolution, 20000 world-unit size, 10000 world-unit height.
  - `storage.SetResolution(int)` reallocates `HeightMap` (`ushort[]`) and
    `ControlMap` (`uint[]`) to `resolution * resolution`.
  - `storage.TerrainSize` — world units across one side (square terrain).
  - `storage.TerrainHeight` — world units of max height; raw heightmap value
    `ushort.MaxValue` maps to exactly this height.
  - `storage.HeightMap[y * resolution + x] = (ushort)value` — direct CPU
    write, row-major.
- Assign `terrain.Storage = storage;` — this triggers `Terrain.Create()`
  internally (allocates GPU textures, builds the collider) as long as the
  component is `Active`.
- **After manually writing to `storage.HeightMap`/`ControlMap` at runtime,
  you must manually push the change**:
  ```csharp
  terrain.SyncGPUTexture();  // CPU heightmap/controlmap -> GPU textures
  terrain.UpdateCollision(
      Terrain.SyncFlags.Height,           // or .Control, or both (flags)
      new RectInt(0, 0, resolution, resolution));  // dirty region
  ```
  `SyncGPUTexture()` and `UpdateCollision()` are the two calls the editor's
  own brush tools use after painting (see `BaseBrushTool.cs`,
  `PaintTextureTool.cs` for the canonical pattern this repo's
  `LuteTerrainGenerator.cs` follows).
- `CompactTerrainMaterial` packs base/overlay texture id + blend factor +
  hole flag into one `uint` for the control map (5+5+8+1 bits). Only
  relevant if painting terrain materials, not needed for heightmap-only
  generation.

### Noise (`Sandbox.Utility.Noise`)

Source: `engine/Sandbox.System/Math/Noise.cs`, `NoiseField.cs`.

- Two APIs: **static convenience functions** (not thread-safe, shared state)
  and **field objects** (thread-safe, more configurable). Prefer field
  objects for anything beyond a one-off sample.
- Static: `Noise.Perlin(x, y[, z])`, `Noise.Simplex(x, y[, z])`,
  `Noise.Fbm(octaves, x, y, z)` — all return `[0, 1]`.
- Field objects — `using Sandbox.Utility;`:
  ```csharp
  var field = Noise.PerlinField(new Noise.FractalParameters(
      Seed: 5633, Frequency: 0.001f, Octaves: 4, Gain: 0.5f, Lacunarity: 2f));
  float n = field.Sample(worldX, worldY);  // [0, 1]
  ```
  - `Noise.PerlinField(...)`, `Noise.SimplexField(...)`, `Noise.ValueField(...)`.
  - Pass a plain `Noise.Parameters` (Seed, Frequency) for single-octave, or
    `Noise.FractalParameters` (adds Octaves, Gain, Lacunarity) for layered
    fractal noise — this is what you want for terrain (see
    `LuteTerrainGenerator.cs` for a 3-layer continent/hills/detail blend).
  - `INoiseField.Sample(x, y)` / `Sample(x, y, z)` / `Sample(Vector2)` /
    `Sample(Vector3)` — always returns `[0, 1]`.

### PlayerController

- Built-in `Sandbox.PlayerController` handles WASD movement, sprint, jump,
  mouse-look, ground detection. **Don't replace it** — extend behavior via a
  sibling component (`LutePlayer` does this for spell-casting/noclip input).
- `[RequireComponent] public PlayerController Controller { get; set; }` on
  your extension component auto-wires the reference.
- `Controller.EyeAngles`, `Controller.WorldPosition`, `Controller.GetComponent<T>()`
  (e.g. to reach the `Rigidbody` for custom noclip flight — see `LutePlayer.ApplyNoclip`).

### Scene file format (`.scene`)

- Plain JSON. Root: `{ "__guid": ..., "GameObjects": [ ... ] }`.
- Each GameObject: `Name`, `Position` (string `"x,y,z"`), `Rotation`
  (quaternion string `"x,y,z,w"`), `Scale`, `Tags`, `Enabled`, networking
  fields (`NetworkMode`, etc.), `Components` (array), `Children` (array,
  recursive).
- Each Component: `__type` (fully-qualified type name, e.g.
  `"Sandbox.DirectionalLight"` or a bare project type name like
  `"LuteGame"`), `__guid`, `__enabled`, plus all its `[Property]` fields.
  **See the PowerShell gotcha above before parsing this with
  `ConvertFrom-Json`.**

### Input

- `Input.Pressed("action")` / `Input.Down("action")` check bound actions.
- Action names are defined in `sbox/ProjectSettings/Input.config`.
- `Input.AnalogMove` gives the raw WASD vector for custom movement (see
  `LutePlayer` noclip).

## Build

```powershell
cd sbox/code
dotnet build lute.csproj
```

Requires the `Documents\bin` junction described above. Expect one harmless
warning (`MSB9008 ... Base Library.csproj does not exist` — a stale
`ProjectReference` from the project template; not used by this repo's code).

## Conventions

- Engine: S&Box (Source 2), C# .NET 10, `Sandbox` namespace,
  `RootNamespace=Sandbox`.
- All gameplay code lives in `sbox/code/`. Do not mix Godot/GDScript or
  Unreal C++ into this project (both were migrated away from — see git
  history "Migrate from Godot/Unreal to S&Box").
- Materials/textures/models are Source 2 binary formats (`.vmat_c`,
  `.vtex_c`, `.vmdl_c`) under `sbox/.sbox/cloud/` and `sbox/Assets/`. Don't
  reference Godot `.tres`/`.tscn` or Unreal `.uasset` paths.
- Prefer `Log.Info`/`Log.Warning` for diagnostics — these show up in the
  editor log that `sbox_verify.ps1` reads.
- Server-authoritative design per PRD §5.1: world state, soul status,
  economy, and quest generation belong server-side; client code is input/
  prediction/HUD only. Not yet enforced in code (single-player scaffolding
  so far) — apply `[Sync]`/`[Broadcast]`/`Host.IsServer` when multiplayer
  features are added.

## Git

- Remote: `https://github.com/drdre72/lute.git`, branch `main`.
- Local git identity for commits in this environment:
  `drdre72 <drdre72@users.noreply.github.com>`.
- `sbox/code/obj/`, `sbox/.vs/`, `sbox/.sbox/transient/`,
  `sbox/.sbox/cloud.db`, `*.slnx`, and `scrap/verify_*.txt`/
  `scrap/sbox_capture_*.png` are gitignored (build artifacts / transient
  verification output — regenerate, don't commit).
