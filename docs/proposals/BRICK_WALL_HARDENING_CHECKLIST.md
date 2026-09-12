# Brick Wall Hardening — Merge Checklist

Use this checklist before merging `brick-wall-hardening` into `main`.

- [ ] `dotnet build sbox/code/lute.csproj` passes.
- [ ] Repo S&Box verification script passes.
- [ ] Clean 3-builder session starts without console errors.
- [ ] First wall brick rendered bounds are approximately 7.48 × 3.94 × 1.57 S&Box units before yaw swaps X/Y.
- [ ] Brick collider bounds match rendered bounds.
- [ ] ReservationManager piece occupancy bounds match rendered/collider bounds.
- [ ] First brick course rests on ground; no half-brick burial.
- [ ] Main road extends north/south.
- [ ] Cross streets extend east/west.
- [ ] Wall task estimated workload is roughly 5,151 placements at current wall height/brick spacing, not 12.
- [ ] Multi-builder assignment is reasonably balanced after corrected wall estimates.
- [ ] TaskCompleted returns the matching builder controller to Idle.
- [ ] TaskBlocked/TaskFailed returns the matching builder controller to Idle.
- [ ] task_1 and wall-corner structural joins still complete under the join policy.
- [ ] No new reservation/placement conflict caused by the road-direction correction.
- [ ] Builder visual body follows the NPC root after teleport/warp.
- [ ] Builder Eyes camera is checked for head clipping; add a forward offset only if live inspection shows it is necessary.
- [ ] Brick material identity is visually verified after geometry scale is correct.
- [ ] Performance impact of thousands of brick GameObjects per segment is measured before extending realistic bricks to the full production village.

Detailed rationale and source-level changes are in `BRICK_WALL_FIX_PASS.md` and `BRICK_WALL_ISSUES.md`.
