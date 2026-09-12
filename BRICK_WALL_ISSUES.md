# Brick Wall Construction — Current Issues & Workflow

## Goal

Three autonomous village builders visibly lay individual wall bricks while the construction system remains deterministic and authoritative. Runtime NPC intelligence remains NLP/world-state driven; no runtime LLM inference is introduced.

## Current brick geometry

- Brick spacing: 0.20 m longitudinal, 0.05 m vertical
- Brick physical size: ~0.19 m long × 0.10 m thick × 0.04 m tall
- Mortar gap: ~0.01 m
- Pattern: running bond with a half-brick offset every other row
- Wall segment: 10 m long, ~5.08 m high (`WallHeight = 200` S&Box units)
- Actual placements per segment: ~5,151 bricks with the current odd-row extra-brick rule
- Build interval: 0.5 s per placed brick

## Root cause of the “large panels” report

**Status: FIX APPLIED ON `brick-wall-hardening`, runtime verification still required.**

The previous implementation treated `models/dev/box.vmdl` as a 1×1×1 model:

```csharp
go.WorldScale = size;
```

That assumption was inconsistent with the rest of the repository, where `box.vmdl` is treated as a 50×50×50 local-unit model. For a requested 7.48 × 3.94 × 1.57 unit brick, the old transform therefore rendered a box roughly 50 times larger in each axis than the requested world dimensions.

This explains why MCP could report a transform scale matching the nominal brick dimensions while the user still saw huge slabs: transform scale was being mistaken for rendered world size.

### Applied fix

`VillageBuilder.SpawnBox()` now uses one size contract for rendering, physics, and occupancy:

```csharp
const float BoxModelNativeSize = 50f;
go.WorldScale = size / BoxModelNativeSize;

var collider = go.AddComponent<BoxCollider>();
collider.Scale = new Vector3( BoxModelNativeSize, BoxModelNativeSize, BoxModelNativeSize );
```

The occupancy AABB continues to use `size` directly, so requested size, rendered bounds, collider bounds, and construction occupancy now describe the same world-space dimensions.

## Brick anchoring bug

**Status: FIX APPLIED, runtime verification required.**

The generic `SpawnBox()` used height to guess whether a piece was a wall or floor. A realistic brick is shorter than the floor threshold, so row-zero bricks were treated like floor tiles and shifted downward by half their height.

An explicit `PieceAnchor` mode was added. Brick placement now passes `PieceAnchor.Base`, so a brick requested at `z=0` rests on the ground instead of being centered below it. Existing non-brick callers retain the legacy automatic behavior for now to avoid changing unrelated structures in the same patch.

## Road tile direction

**Status: FIX APPLIED, runtime verification required.**

The village grammar uses road rotation as the road-width axis: main N/S roads are `Rotation=0`, cross streets are `Rotation=90`. The executor previously advanced tiles along that same axis, which made each segment grow sideways.

`BuildRoadSection()` now advances tiles along the perpendicular local Y axis while retaining the existing road-width rotation. This makes `Rotation=0` advance N/S and `Rotation=90` advance E/W.

## Workload estimation

**Status: FIX APPLIED FOR VillageBuilder/director registration.**

The old estimate of 12 pieces per wall segment was inherited from the panel-based wall implementation. With realistic bricks, a wall segment is ~5,151 placements.

`VillageBuilder` now computes the brick estimate from wall height and brick spacing, uses it for total estimates and builder partitioning, and overwrites each registered `DirectedTask.EstimatedPieces` with the corrected value. This prevents the ConstructionDirector from balancing thousands-of-bricks wall jobs as if they were 12-piece tasks.

The local fallback estimator still present inside `ConstructionDirector` remains legacy code; director tasks registered by `VillageBuilder` are corrected immediately after registration.

## Builder camera/body transform issue

**Status: PARTIAL FIX APPLIED.**

Builder visual bodies were parented to the NPC root and then assigned `WorldPosition = Vector3.Zero`. That can separate the rendered citizen from its moving/warping NPC root and makes first-person camera inspection unreliable.

The child body now uses `LocalPosition = Vector3.Zero` in both village-builder and normal-builder spawn paths. The `Eyes` camera remains at local `(0,0,64)` pending live verification; if it still clips into the face, add a small local forward offset after confirming the citizen forward axis in S&Box.

## Material issue

**Status: UNRESOLVED / VERIFY AFTER SCALE FIX.**

`brick_wall.vmat` currently references the stone texture set. The repository also contains `.vtex` descriptors for `brick_color`, `brick_normal`, and `brick_rough`, but the current material does not reference them.

Do not assume the MCP-reported `stone_wall` name is engine deduplication until the actual rendered material path is verified. First confirm physical brick scale. Then test the override with an unmistakable temporary material/tint or wire the brick texture resources explicitly if their source PNGs are present and compiled.

## Performance architecture risk

**Status: OPEN — intentionally not redesigned in this repair pass.**

At ~5,151 bricks per 10 m wall segment and roughly 100+ wall segments, the current design can create hundreds of thousands of:

- GameObjects
- ModelRenderers
- BoxColliders
- permanent occupancy entries

That is not a good final architecture. The safer next design is to keep brick-by-brick **construction events/visual progress** while batching completed courses or segments into a small number of render/collision objects and using segment-level permanent occupancy. This should be handled as a separate performance pass after correctness is verified.

## Timing reality

At 0.5 s per brick, one ~5,151-brick wall segment takes about 42.9 minutes for one builder. Three builders work on separate tasks, so they do not reduce a single segment to ~14 minutes. The previous “~25 minute full village” figure from the old 0.1 s / low-piece-count system no longer applies to realistic per-brick construction.

## Verification checklist

1. Build:

```powershell
cd C:\Users\Shadow\Documents\lute
dotnet build sbox/code/lute.csproj
```

2. Sync changed runtime files to the S&Box addon and restart play mode.

3. Start from a fresh runtime session/save.

4. Inspect one new `Village_Wall*` brick and verify **rendered/world bounds**, not just `WorldScale`:

- expected world size: about 7.48 × 3.94 × 1.57 S&Box units before yaw swaps X/Y
- row-zero brick bottom should be approximately at task ground Z
- collider bounds should match rendered bounds
- occupancy bounds should match both

5. Visually inspect a near wall. Bricks should be physically small and separated by ~1 cm mortar gaps.

6. Verify roads:

- main road extends north/south
- cross streets extend east/west
- tiles do not fan sideways

7. Verify multi-builder assignment logs. Wall tasks should carry ~5k estimated pieces rather than 12.

8. Verify builder cameras after warp. If camera still intersects the citizen head, measure the citizen facing axis and add a small local forward offset.

9. Re-run reservation/collision probes and confirm the earlier event-driven completion, failure recovery, join-zone, and occupancy invariants remain intact.

## Files touched by the repair pass

- `sbox/code/Building/VillageBuilder.cs`
- `sbox/code/NPC/NPCSpawner.cs`
- `BRICK_WALL_ISSUES.md`
- `BRICK_WALL_FIX_PASS.md`

See `BRICK_WALL_FIX_PASS.md` for the exact checklist and branch-level change log.
