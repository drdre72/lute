---
agent: devin-local
session: awesome-botany
created: 2026-09-07T04:16:52Z
---
# Brick-Laying Polish: Sections, Ghost, Animation, Sound, Mortar Glow

## Summary
Add a deterministic section-based wall-building system (1×1, 2×2, 3×3, 4×4 module sections) with per-brick ghost highlight, single-brick arm-place animation, soft sandy plop sound, and a mortar-fill glow effect when a section solidifies.

---

## Current State

- **Wall segments**: 2m × 0.5m × 2m, built one brick at a time at `BuildInterval` (0.5s).
- **Brick model**: Facepunch `brick_single_04` cloud asset, scaled to `BrickModuleSize`.
- **NPC facing**: `FaceWall()` rotates NPC toward wall center.
- **Animation**: `PlayLayAnimation()` cycles the citizen `duck` parameter (crouch → stand).
- **Sound**: None.
- **Ghost/preview**: None.
- **Section concept**: Walls are generated as ~2m segments in `VillageGrammar.GenerateWallTasks()`. No 1×1/2×2/3×3/4×4 section selection.
- **Finalization**: `WallSegmentState` state machine: `Planned → BrickLaying → FinalizationEligible → Finalized`. `FinalizeWall()` collapses bricks to one mesh.
- **Glow effect**: `WarpEffect` exists for teleport poofs (tinted box that expands and fades). Reusable pattern.

---

## Plan

### Phase 1: Section Selection System
**Goal**: NPC deterministically selects a section size (1×1, 2×2, 3×3, or 4×4 modules) for each wall build task.

- Add `SectionSize` enum: `OneByOne`, `TwoByTwo`, `ThreeByThree`, `FourByFour` (in module units, not meters).
- Add `[Property] SectionSize SelectedSection` to `VillageBuilderController` (deterministic per-NPC, based on `BuilderId` + task `LayoutSeed`).
- Map section size to wall dimensions:
  - 1×1 = 1 module × 1 course × 1 wythe (single brick)
  - 2×2 = 2 modules × 2 courses × 1 wythe (4 bricks)
  - 3×3 = 3 modules × 3 courses × 1 wythe (9 bricks)
  - 4×4 = 4 modules × 4 courses × 1 wythe (16 bricks)
- The section is a sub-unit within the existing 2m wall segment. The NPC builds one section at a time, then moves to the next section.
- **Files**: `VillageBuilderController.cs`, `VillageGrammar.cs` (add enum), `VillageBuilder.cs` (section-aware brick loop).

### Phase 2: Brick Ghost Highlight
**Goal**: Before each brick is placed, a translucent ghost version appears at the target position, then gets replaced by the real brick.

- Add `BrickGhost` component (similar to `WarpEffect`):
  - Spawns a `brick_single_04` model at the target position.
  - Tinted with a translucent highlight color (e.g., cyan 0.5 alpha).
  - Fades in over ~0.15s, holds, then the real brick spawns and the ghost fades out over ~0.1s.
- In `BuildWallSegment`, before each `SpawnBox` call, spawn the ghost at the brick position.
- The ghost auto-destroys when the real brick appears.
- **Files**: New `BrickGhost.cs`, `VillageBuilder.cs` (call ghost spawn before `SpawnBox`).

### Phase 3: Single-Brick Arm-Place Animation
**Goal**: Replace the crouch-cycle with a proper "arm down" placement motion per brick.

- Enhance `PlayLayAnimation()` to drive a single arm-down motion synced to `BuildInterval`:
  - Phase 0 (0–0.25s): arm raises slightly (preparation).
  - Phase 1 (0.25–0.5s): arm swings down to place position.
  - Phase 2 (0.5s): brick snaps in, arm returns.
- Use citizen animgraph parameters: `b_grounded=true`, `move_x=0`, and cycle `duck` for the body dip. If arm parameters exist (`aim_*` or `hand_*`), drive them; otherwise keep the duck cycle but sync it to the brick placement moment (not a continuous sine).
- The key change: the animation should be **per-brick** (one cycle per `BuildInterval`), not continuous.
- **Files**: `VillageBuilderController.cs` (`PlayLayAnimation`).

### Phase 4: Soft Sandy Plop Sound
**Goal**: Play a soft "sandy plop" sound at each brick placement position.

- Create a `SoundEvent` resource for the plop sound. Since we don't have a custom sound file, use an existing S&Box sound event or create a minimal `.vsnd` from a short noise burst.
- In `SpawnBox` (wall brick path), after placing the brick, call:
  ```csharp
  Sound.Play( BrickPlopSound, worldPos );
  ```
  where `BrickPlopSound` is a cached `SoundEvent` loaded once.
- If no suitable sound asset exists, use `Sound.Play( "sandbox.sounds.ui.click", worldPos )` as a placeholder and note it for replacement.
- **Files**: `VillageBuilder.cs` (sound load + play), possibly new sound asset.

### Phase 5: Mortar-Fill Section Glow
**Goal**: When a section (1×1, 2×2, 3×3, 4×4) is fully built, a warm glow sweeps over the section and mortar fills visibly.

- Add `SectionGlowEffect` component (extends `WarpEffect` pattern):
  - Spawns a tinted box (warm amber/gold) covering the section bounds.
  - Expands slightly and fades over ~0.8s.
  - Simultaneously, a "mortar fill" visual: the gaps between bricks in the section get a lighter tint (material override or a thin overlay mesh).
- Trigger: when the last brick of a section is placed, call `SectionGlowEffect.Spawn(scene, sectionCenter, sectionBounds, tint)`.
- The glow is purely visual — the structural state (`WallSegmentState`) is not changed by the glow. Finalization remains a separate director-approved step.
- **Files**: New `SectionGlowEffect.cs`, `VillageBuilder.cs` (trigger after section complete).

### Phase 6: NPC Section Selection Logic
**Goal**: The NPC deterministically chooses which section to build next within a wall segment.

- Track section progress per wall task: `_currentSection` index, `_sectionsInTask` list.
- Divide each 2m wall segment into sections based on the selected `SectionSize`:
  - 1×1: 8 sections per course (8 modules × 1 course)
  - 2×2: 4 sections per 2 courses
  - 3×3: 2 sections per 3 courses (with remainder)
  - 4×4: 2 sections per 4 courses
- The NPC builds sections in deterministic order (bottom-left to top-right, by course then by module).
- After all sections in a task are complete, the task transitions to `FinalizationEligible`.
- **Files**: `VillageBuilder.cs` (section tracking), `VillageBuilderController.cs` (section state).

---

## Verification

After each phase:
1. `dotnet build sbox/code/lute.csproj` — 0 errors.
2. Sync changed `.cs` files to `sbox-public-clean\game\addons\lute\code\Building\`.
3. Restart play mode via MCP.
4. Capture screenshot from editor camera or 2nd Camera.
5. Send to GPT vision via `agent/gpt_eyes.py`.
6. Verify:
   - Ghost highlight appears before each brick.
   - Arm animation syncs to brick placement.
   - Plop sound plays (check via logs or audio confirmation).
   - Section glow appears when a section completes.
   - Running bond pattern preserved.
   - No gaps, no clipping, no floating bricks.

---

## Commit Strategy

- Commit after each phase (or grouped phases) with descriptive messages.
- Push to `origin/main` after verification.
- Do not commit scratch scripts, screenshots, or sound assets unless intended.

---

## Files to Create/Modify

| File | Action |
|------|--------|
| `sbox/code/Building/VillageGrammar.cs` | Add `SectionSize` enum |
| `sbox/code/Building/VillageBuilderController.cs` | Section selection, arm animation |
| `sbox/code/Building/VillageBuilder.cs` | Section-aware brick loop, ghost spawn, sound play, glow trigger |
| `sbox/code/Building/BrickGhost.cs` | **New** — ghost highlight component |
| `sbox/code/Building/SectionGlowEffect.cs` | **New** — section mortar glow component |
| `sbox/code/Building/WarpEffect.cs` | No change (reference pattern) |

---

## Notes

- All NPC behavior remains **deterministic** — no LLM for gameplay. Section selection is based on `BuilderId` + `LayoutSeed`.
- The `BrickSlot` topology remains authoritative. Ghosts and glows are visual only.
- The `ConstructionDirector` remains the authority for task authorization and finalization.
- Sound assets: use placeholder S&Box sounds if no custom plop sound is available. Note for replacement.
- The "1 inch off the ground" observation: bricks are placed at `z = row * BrickModuleZ` with `PieceAnchor.Base`, which adds `size.z * 0.5f`. This may cause a slight float. Investigate and fix if needed.
