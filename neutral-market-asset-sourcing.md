# Neutral Market — Asset Sourcing Guide (s&box / Source 2)

Pipeline reminder: s&box's Asset Browser imports FBX/glTF/OBJ directly and auto-generates a `.vmdl`; drop PBR textures (albedo/normal/rough/metal) alongside and it compiles them to `.vtex`, wiring into a `.vmat` with minor tweaks. Everything below is free unless flagged. Source 2 units are inches — most free packs are metric, so expect a rescale pass on import to match your 12m wall / 3m moat spec.

## Step 0 — do this before anything else in this doc

**Test-import the "400+ Medieval Village pack" (Marco Maria Rossi, free via cgchannel/itch) first, on its own, before sourcing anything else.** It's listed below for perimeter walls, housing, and stalls — if it survives the preflight check, it becomes your backbone kit and most of the rest of this doc shrinks to "fill in stalls, well, sigil, particles." If it fails, you'll know in twenty minutes instead of after planning three sections around it.

**Import preflight checklist (run this on every new pack, not just this one):**
1. Import a single representative piece (one wall segment or one stall).
2. Check the s&box console for material-name rejections — the archway work already hit the FBX dotted-name gotcha (`root.0.0`), so assume it'll recur until proven otherwise.
3. Confirm `model.Bounds` matches the expected real-world scale after import (this is where the inches/metric mismatch shows up concretely, not just as a warning in this doc).
4. Confirm the piece takes a `ModelCollider` or bakes a usable `PhysicsHullFromRender` cleanly — a wall you can walk through defeats the monument. Budget this as a per-asset step, not a pass at the end.

Only after a pack clears all four should you commit to importing the rest of it.

## Blocker to resolve before moat/bridge work — water

Before sourcing or placing anything for the moat, verify s&box's built-in water shader/material actually renders correctly at **runtime**, not just in the editor, and that it can represent a sunk 3m-deep volume with vertical walls rather than just a flat plane. Source 2's water shader is planar by default; if it can't do a true sunk volume, that's a design fork, not a texture problem:
- **Option A:** fake the depth — dark/murky material on the moat floor + a separate reflective plane at water level.
- **Option B:** rework the moat as a shallower feature that a planar shader can sell convincingly.

Decide this before spending time on bridge assets — the bridge geometry depends on which option you pick.

## Start here — already native to your project
- **Facepunch CitizenAssets** — the base rigged NPC/player model shipped with s&box itself. This is your fastest path to guild guards, merchants, the blacksmith, etc. — reskin/retexture rather than modeling humans from scratch.
- **Mounted Source 2 titles** — if you (or the s&box install) have CS2 or Half-Life: Alyx content mounted, their stone/ruin/temple materials (e.g., CS2's `de_ancient`) are usable as a base for dressed-limestone walls and give you Source-2-native normal/AO maps for free.

## Perimeter — walls, towers, battlements
- **Primary candidate: the 400+ Medieval Village pack** (see Step 0). If it clears preflight, use its wall/tower/gate sections as the single texture language for the whole perimeter rather than mixing in loose texture sets — a modular kit that imports clean beats hand-dressing individual boxes with separate materials.
- **Fallback if it fails preflight:** Sketchfab (filter Downloadable + free license) — search "modular medieval wall kit," "castle tower modular."
- **Poly Haven** (polyhaven.com) / **AmbientCG** (ambientcg.com) — CC0 limestone/sandstone/masonry PBR sets, useful either as the kit's texture source or to break up tiling on top of it.

## Moat & bridges
- Water: see blocker above — resolve before sourcing bridges.
- **Poly Haven / Sketchfab** — search "stone bridge low parapet" for the four crossing bridges, once the water approach is decided.

## Central plaza — stalls, crates, barrels, awnings
- **"Medieval Stylized Assets pack Free"** (alefranart, itch.io, pay-what-you-want) — glTF with textures, includes stalls; stylized look rather than realistic.
- **Sketchfab** — search "medieval market stall free," "medieval crate barrel pack free" (filter CC0/CC-BY).
- **OpenGameArt** (opengameart.org, tag: medieval) — mixed but has CC0 crates/barrels/sacks for stall dressing.

## Inner ring — crafting workbenches: scope decision needed, not just a sourcing task

No single pack covers all eight stations, so this is 8 separate search/import/scale/collision passes if you build all of them now. Before doing that work, decide deliberately whether v1 needs all eight or just enough to prove the loop (e.g. forge/anvil, workbench, alchemy table). This isn't purely an asset-sourcing shortcut — check it against PRD §5.2 first: if the economic watcher routes profession quests through all 8 stations, shipping only 3 means 5 professions have nowhere to send players. If that's fine for v1 (those professions come later anyway), cut to 3. If not, budget for all 8 now.

Per-station search terms (filter CC0/free license each time):
- Anvil + forge → "blacksmith forge anvil free"
- Loom → "medieval loom model free"
- Alchemy table → "alchemist table props free"
- Carpenter's bench → "carpenter workbench medieval"
- Tannery rack → "tannery rack model free"
- Smelter → "medieval smelter kiln free"
- Grinding wheel → "grindstone wheel model free"

## Inner ring — housing modules
- Same 400+ Medieval Village pack and Medieval Stylized Assets pack above both include modular 2-story building pieces — reuse the same kit as the perimeter for visual consistency between houses and curtain wall.

## Central well
- Sketchfab — search "stone well free" (CC0 results exist). Simple enough geometry (cylinder + rim + roof) that it's also a reasonable one-off custom build if nothing free fits your scale.

## Custom-only pieces — no good free source, build these
- **Guild sigil (balanced scale on undyed linen)** — unique heraldry, won't exist premade. Block a simple banner mesh in Blender (flat plane or light cloth-sim drape), then paint/vector the scale icon in Photoshop/Affinity/Inkscape and bake it as the banner's albedo texture. The same flat texture doubles as the carved-medallion decal above each gate — apply it to a shallow relief/normal-mapped disc rather than modeling true stone carving.
- **1km-visible tower torches** — a Source 2 particle-system + point-light job, not an asset. Author it directly; optionally reference an HLA fire particle if that content is mounted, as a starting point for the flame shape.

## Collision — per-asset, not an afterthought
Every imported piece above (wall segments, towers, bridges, stalls, workbenches, housing) needs its own `ModelCollider` or baked `PhysicsHullFromRender` — this is a per-piece step during import, the same way it was solved for the Acropolis, not a single pass done at the end once everything looks right.

## License notes
- Prioritize CC0 sources (Poly Haven, AmbientCG, CC0-tagged OpenGameArt entries) since those need zero attribution and have no commercial restriction.
- Itch.io "pay-what-you-want" packs are generally fine for a personal/hobby project, but check each page's license line before any public release or monetization.
