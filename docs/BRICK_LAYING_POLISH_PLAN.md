---
agent: devin-local
session: awesome-botany
created: 2026-09-07T04:16:52Z
---
# Brick-Laying Polish: Revised Plan (following Prof's recommendations)

## Summary
Fix foundational brick visual invariance first (half-brick scaling, finalization fidelity, anchor, reference test), then build a reusable masonry work system: WorkPatch, placement event, animation, audio, mortar, and optional debug overlays.

---

## Revised Implementation Order

### Phase 1: Brick Visual Invariance (FOUNDATIONAL — do this first)

**1a. Fix full vs half cloud-brick scaling.**
- `SpawnBox()` currently scales every wall brick to `BrickModuleSize` (25×12.5×6.25cm), ignoring the requested `size`.
- Full bricks: scale to `BrickModuleSize` (25×12.5×6.25cm) — correct.
- Half bricks: scale to `(BrickModuleX * 0.5, BrickModuleY, BrickModuleZ)` = (12.5×12.5×6.25cm).
- Derive the render envelope from `BrickForm` (Full vs Half), not a fixed `BrickModuleSize`.
- If Facepunch has a half-brick variant in the cloud pack, use it; otherwise nonuniform X scaling is acceptable initially.
- **Files**: `VillageBuilder.cs` (SpawnBox scaling logic).

**1b. Make finalization preserve `brick_single_04` visual.**
- `FinalizeWall()` currently generates box vertices and assigns `Material.Load(WallMaterial)` — this collapses the beautiful cloud brick to flat boxes.
- Finalization should be **visually lossless**. The player shouldn't see a visual downgrade.
- Option A: Don't collapse to a mesh — keep the individual `brick_single_04` GameObjects (they're already optimized as cloud assets).
- Option B: If collapse is needed for performance, bake the cloud brick appearance into the final mesh (capture the model's materials/normals, not just box geometry).
- **Files**: `VillageBuilder.cs` (FinalizeWall), `ReferenceWallTest.cs` (FinalizeWall).

**1c. Fix reference-wall fixture to use same cloud scaling as production.**
- `ReferenceWallTest.SpawnBrick()` loads `brick_single_04` but scales using `size / 50` (dev/box pattern).
- Production uses `renderer.Model.Bounds.Size`. The test must match production.
- **Files**: `ReferenceWallTest.cs` (SpawnBrick scaling).

**1d. Fix ground-anchor (1-inch float).**
- Bricks placed at `z = row * BrickModuleZ` with `PieceAnchor.Base`, which adds `size.z * 0.5f`.
- Investigate and fix so bricks sit exactly on the ground at course 0.
- **Files**: `VillageBuilder.cs` (SpawnBox anchor logic).

### Phase 2: MasonryWorkPatch (not "Section")

**Goal**: Group existing `BrickSlot`s into work patches — NOT new construction geometry.

- Add `MasonryWorkPatch` struct/class:
  ```csharp
  MasonryWorkPatch
  {
      IReadOnlyList<BrickSlot> Slots;
      int StartCourse;
      int CourseCount;
      int StartModule;
      int ModuleCount;
      int Wythe;
  }
  ```
- A 4×4 patch means "Builder, work these 16 already-valid BrickSlots" — not "generate a 4×4 mini-wall."
- Patches clip to available slots (no remainder problem for 3×3 in 8-module walls).
- **No `SectionSize` enum in `VillageGrammar`** — patches are a work-organization layer, not a geometry layer.
- **Files**: New `MasonryWorkPatch.cs`, `VillageBuilder.cs` (patch-aware brick loop), `VillageBuilderController.cs` (patch state).

### Phase 3: Placement Event + Per-Brick Animation

**Goal**: Make hand contact the authoritative visual placement moment.

- Add `OnBrickPlacementContact(BrickSlot)` event — everything visual fires from this:
  - BrickSlot becomes visually occupied
  - `brick_single_04` appears
  - Placement sound plays
  - Mortar/dust effect fires
- Replace `await DelaySeconds(0.5f); SpawnBrick();` with event-synchronized placement:
  ```
  0.00  acquire brick
  0.10  arm moves toward slot
  0.35  hand approaches wall
  0.42  CONTACT EVENT → brick appears, sound plays, dust fires
  0.50  arm releases/returns
  ```
- Animation is **one action per brick**, not continuous crouch cycle.
- **Files**: `VillageBuilderController.cs` (animation), `VillageBuilder.cs` (placement event).

### Phase 4: Masonry Audio (varied, not one plop)

**Goal**: Subtle varied positional sound set, not a metronome.

- Use 4–8 subtle variants: `brick_place_01` through `brick_place_04`, `mortar_press_01`, etc.
- Deterministic volume/pitch variation per placement.
- Positional attenuation via `Sound.Play(SoundEvent, Vector3 position)`.
- **No `sandbox.sounds.ui.click`** beyond smoke test — wrong feel.
- **Files**: `VillageBuilder.cs` (sound load + play), possibly new sound assets.

### Phase 5: Mortar Representation (wet→dry, not glow)

**Goal**: Cheap patch-level wet→dry joint/backing effect.

- Mortar as a thin backing layer behind/between brick beveled edges:
  ```
  front view
  ╔══════╦══════╦══════╗
  ║brick ║brick ║brick ║
  ╠══════╬══════╬══════╣
  ║brick ║brick ║brick ║
  ╚══════╩══════╩══════╝
     ↑ thin mortar backing behind brick edges
  ```
- During work: mortar = dark/wet.
- After completion: mortar gradually → lighter/dry.
- One cheap mesh per WorkPatch.
- **No supernatural amber glow** in normal gameplay.
- **Files**: New `MortarLayer.cs`, `VillageBuilder.cs` (mortar spawn per patch).

### Phase 6: Optional Ghost/Debug Overlays

**Goal**: Cyan ghost and completion glow as DEBUG switches, not default gameplay.

- `DebugConstructionVisuals = true` → cyan BrickSlot ghost before placement.
- `ShowWorkPatchCompletionGlow = true` → patch completion glow for development.
- Useful for Devin/GPT Eyes verification and possible later player building mode.
- In normal NPC gameplay: NPC has brick → arm approaches → dust/mortar disturbance → brick appears at hand contact.
- **Files**: New `BrickGhost.cs` (debug), `VillageBuilder.cs` (debug switches).

### Phase 7: NPC Work-Patch Reasoning

**Goal**: Choose patch from reach, height, obstruction, skill, collaboration — not pseudorandom seed.

```
available BrickSlots
+ reach
+ work height
+ obstruction
+ other-builder claims
+ craftsmanship
────────────────────
WorkPatch
```

- Ground-level clear wall: 4×4
- Tight corner: 2×2
- Repair around missing bricks: 1×1
- Working from scaffold: 3×3
- Another mason occupying right side: left-side 2×4
- Master mason doing detailed bond: smaller deliberate patch
- Still zero LLM — deterministic rules.
- **Files**: `VillageBuilderController.cs` (patch selection logic).

---

## Verification

After each phase:
1. `dotnet build sbox/code/lute.csproj` — 0 errors.
2. Sync changed `.cs` files to `sbox-public-clean\game\addons\lute\code\Building\`.
3. Restart play mode via MCP.
4. `python agent\wall_camera.py` — set camera to wall view.
5. Capture screenshot, send to GPT vision via `agent/gpt_eyes.py`.
6. Verify visual invariance: half bricks correct, finalization lossless, no float, running bond preserved.

---

## Files to Create/Modify

| File | Phase | Action |
|------|-------|--------|
| `sbox/code/Building/VillageBuilder.cs` | 1,2,3,4,5,6 | Scaling, finalization, patches, placement, sound, mortar, debug |
| `sbox/code/Building/ReferenceWallTest.cs` | 1 | Fix cloud scaling to match production |
| `sbox/code/Building/MasonryWorkPatch.cs` | 2 | **New** — work patch grouping |
| `sbox/code/Building/VillageBuilderController.cs` | 2,3,7 | Patch state, animation, reasoning |
| `sbox/code/Building/MortarLayer.cs` | 5 | **New** — mortar backing effect |
| `sbox/code/Building/BrickGhost.cs` | 6 | **New** — debug ghost overlay |

---

## Key Principles (from Prof)

- **BrickSlot is truth.** GameObjects are temporary representation. WorkPatches group slots, not generate geometry.
- **Finalization must be visually lossless.** No downgrade from `brick_single_04` to flat boxes.
- **Placement is event-synchronized.** `OnBrickPlacementContact` is the authoritative moment, not `await DelaySeconds`.
- **No supernatural effects in normal gameplay.** Ghost and glow are debug-only.
- **Mortar is a real material layer**, not a magic glow.
- **NPC patch choice is contextual**, not pseudorandom.
- **All NPC behavior remains deterministic** — no LLM for gameplay.
