# Brick Wall Hardening — Fix Pass Checklist

Branch: `brick-wall-hardening`
Base: `main` at `2f8de959bc6c343cee582f4bba47ce0987077473`

This file records the source-level repair pass requested after reviewing `BRICK_WALL_ISSUES.md` and the latest construction changes. It distinguishes fixes actually applied from items deliberately deferred until S&Box runtime verification.

## Applied fixes

- [x] **Correct `box.vmdl` render scale.** `models/dev/box.vmdl` is treated as a 50×50×50 local-unit model. `SpawnBox()` now sets `WorldScale = requestedWorldSize / 50` instead of treating requested dimensions as raw transform scale.
- [x] **Align renderer, collider, and occupancy dimensions.** Box colliders now use 50 local units so the scaled collider resolves to the same requested world dimensions used by the renderer and placement AABB.
- [x] **Stop realistic bricks from being classified as floors.** Added explicit `PieceAnchor` semantics; wall bricks use `PieceAnchor.Base` so the first course rests on the task ground plane.
- [x] **Correct brick task piece count.** Running-bond odd rows add one extra brick; `task.TotalPieces` and estimates now account for those extra placements.
- [x] **Replace stale 12-piece wall estimates.** `VillageBuilder` now derives wall workload from wall height and brick spacing (~5,151 placements at current settings).
- [x] **Use corrected estimates for multi-builder partitioning.** Wall-heavy work is no longer balanced as if each wall were a 12-piece task.
- [x] **Propagate corrected estimates into Director scheduling.** Immediately after `ConstructionDirector.RegisterTask`, the corresponding `DirectedTask.EstimatedPieces` is overwritten with the corrected builder-side estimate.
- [x] **Fix road tile travel direction.** Road tiles now advance along the axis perpendicular to the configured width rotation, matching the existing grammar convention: rotation 0 produces N/S travel, 90 produces E/W travel.
- [x] **Fix builder visual child positioning.** Citizen `Body` children now use `LocalPosition = Vector3.Zero` instead of assigning world zero after parenting.
- [x] **Correct issue documentation.** `BRICK_WALL_ISSUES.md` now records the scale mismatch as the leading root cause and removes the unsupported assumption that MCP transform scale proves world-space brick size.

## Intentionally deferred

- [ ] **Per-brick batching/instancing.** The current one-GameObject/renderer/collider/occupancy-entry-per-brick architecture can grow to hundreds of thousands of objects. This should be a separate performance pass after geometry correctness is proven.
- [ ] **Segment-level final collision/occupancy.** Do not remove per-brick collision/occupancy until a replacement final-segment collision/occupancy path exists and is verified.
- [ ] **Builder-eye forward offset.** The parent/local transform bug is fixed, but the camera remains at local `(0,0,64)` until live inspection establishes the citizen model's forward axis and whether head clipping remains.
- [ ] **Brick material resource cleanup.** `brick_wall.vmat` still references the stone texture set. Verify the physical scale first, then explicitly wire brick textures or use a temporary unmistakable diagnostic material.
- [ ] **Remaining building footprint conflicts (`task_199`, `task_235`).** These are separate grammar/blueprint-footprint issues and were not mixed into the brick-wall repair.
- [ ] **Remove/replace legacy `ConstructionDirector.EstimatePieces()` wall=12 fallback.** VillageBuilder registration now corrects Director estimates for this path, but the Director helper itself remains stale for any future non-VillageBuilder caller.

## Runtime verification required before merge

The source changes on this branch have **not** been compiled or play-tested by this ChatGPT session. Before merging:

1. `dotnet build sbox/code/lute.csproj`
2. Run the repository S&Box verification script.
3. Start a clean 3-builder play session.
4. Inspect a newly placed brick's actual rendered/world bounds. Expected dimensions are approximately `7.48 × 3.94 × 1.57` S&Box units before yaw swaps X/Y.
5. Verify the brick collider bounds match rendered bounds.
6. Verify the construction occupancy AABB matches both.
7. Verify row-zero brick bottoms sit on the task ground plane rather than below it.
8. Verify the main road runs N/S and cross streets E/W.
9. Verify Director assignment logs show wall workloads around 5k pieces rather than 12.
10. Verify `TaskCompleted`, `TaskBlocked`, and `TaskFailed` controller transitions still work with all three builders.
11. Re-run reservation/collision diagnostics and confirm task_1 structural joins remain allowed without broad collision weakening.
12. Inspect builder `Eyes` cameras after warp and confirm the visual body follows the NPC root.

## Expected impact

The highest-confidence behavioral correction is the `box.vmdl` scale contract. Under the old code, a requested ~19 cm brick could render roughly 50× too large per axis because the requested world dimensions were assigned directly as transform scale. The new code makes requested world size the single source of truth for rendering, collision, and occupancy.

The next likely bottleneck after correctness is performance: realistic walls at the current dimensions require roughly 5,151 placements per 10 m segment and hundreds of thousands of brick objects across the full perimeter. That optimization should be solved deliberately, not hidden inside this correctness patch.
