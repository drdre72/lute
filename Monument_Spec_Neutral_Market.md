# Monument Spec: The Neutral Market

**Status:** Draft v0.1
**Type:** Fixed-location monument (Rust-style POI), first of the game's monument set
**Engine:** s&box (Source 2)

---

## 1. Concept

A circular, fortified market ring belonging to no single town — neutral ground where any faction can trade, craft, and rest without fear of raiding. Visually it should read as a cross between a Rust monument (functional, defensible, gameplay-dense) and a medieval fairground-meets-castle bailey: a ring of stalls at the heart of a small fortress.

**Working lore**: administered by an independent merchant's guild (NPC faction) rather than any town — the guild's guards staff the walls and towers, and the guild's neutrality is the reason raiders and townsfolk alike can walk in the same gate. (Flag if you'd rather it be jointly run by nearby settlements instead.)

## 2. Site Plan (concentric rings, center-out)

| Ring | Radius (from center) | Contents |
|---|---|---|
| Central plaza | 0 – 60m | Market stalls, arranged in neat radial rows/wedges |
| Inner ring | 60 – 110m | Crafting workbenches/stations (forge, tannery, loom, etc.) alternating with NPC housing |
| Wall band | 110 – 140m | Stone curtain wall, walkable battlements on top, murder holes over each gate tunnel |
| Moat | 140 – 170m | 3m deep, water or dry-ditch (design choice), full ring, only crossed via the 4 bridges |

**Entrances**: 4 bridges at N/E/S/W, each crossing the moat and passing through a gatehouse in the wall. Each gatehouse has murder holes in its ceiling.

**Watchtowers**: 8 total, in pairs flanking each of the 4 gates (one tower on each side of the bridge/gate), seated on the wall band, taller than the curtain wall, accessible via stairs/ladder from the battlement walkway.

## 3. Zone-by-Zone Detail

### 3.1 Central Plaza (market stalls)
- Radial or grid arrangement of stalls, walkable aisles between rows, likely 4-8 wedge "districts" (e.g. general goods, weapons/armor, food, exotic/rare) so it reads as organized rather than a scatter.
- A single open square or fountain/well at the dead center as a visual anchor and a natural player meetup point.
- NPC vendor stalls tie directly into the town/job system from the main PRD — each stall can be "owned" by a merchant-class NPC with its own persona (sex/class/version) and dialogue.

### 3.2 Inner Ring (workbenches + housing)
- Alternating segments: crafting stations (forge, tannery, carpentry bench, alchemy table, etc.) and small NPC housing units.
- Each station ties into the crafting system's station-specialization mechanic — this is where players (or resident specialist NPCs) actually produce refined/masterwork goods.
- Housing should be modest, functional — this ring reads as the "working" layer between the commerce core and the defensive shell.

### 3.3 Wall Band (battlements)
- Full curtain wall, walkable top surface (battlement walkway), crenellations along the outer edge.
- Murder holes specifically over the 4 gate tunnels — openings in the gatehouse ceiling for dropping/firing on anyone in the tunnel.
- Wall height: tall enough to dominate the skyline from outside, walkway wide enough for guard NPC patrol paths and player traversal.
- Access points: stairs/ramps up to the walkway near each gatehouse, so guards (and players) can move from ground level to battlements without a full lap.

### 3.4 Moat
- 3m deep, full ring, no land bridge except the 4 built bridges — anyone approaching must use a gate.
- Decide early: water-filled (visual/atmospheric, may need swim mechanics or hazard) vs. dry ditch (simpler, still blocks movement, avoids water-rendering overhead).

### 3.5 Bridges & Gates (x4)
- Each bridge spans the moat and feeds directly into a gatehouse tunnel through the wall.
- Gatehouse: short enclosed passage, murder holes above, likely a closable gate/portcullis for lore/gameplay flexibility (even if always-open in v1, worth building the socket for it).

### 3.6 Watchtowers (x8)
- Two per gate, flanking the bridge/gate on either side, built into or against the wall band.
- Taller than the curtain wall for sightlines over the approach and the moat.
- Interior ladder/stairs connecting ground → wall walkway → tower top.
- Natural home for guard-NPC posts and a strong vantage point for players.

## 4. s&box Asset Plan (whitebox-first)

Recommended build order to get a playable/testable blockout before any bespoke art pass:

1. **Blockout with primitives**: use basic geometry (cylinders for the plaza/tower footprints, box brushes for wall segments, flat planes for the moat and plaza floor) to lock the site plan at real scale before touching detailed assets. This is exactly the ring layout drawn in the site plan above — reproduce it 1:1 as gray-box geometry first.
2. **Check s&box's built-in/marketplace content** for anything reusable off the shelf: modular wall/tower kits, medieval prop packs, and generic "castle" or "village" asset packages available through s&box's asset system — reuse rather than model from scratch wherever a package fits, even if it's a placeholder look for now.
3. **Moat & bridges**: block moat as a ring-shaped depression (terrain cut or geometry), bridges as simple plank/stone deck modules spanning it — functional traversal first, detail later.
4. **Wall & battlements**: modular wall segment repeated around the ring, a walkable top surface, crenellation prop repeated along the outer edge, gate tunnel sections with a ceiling gap for murder holes.
5. **Towers**: reuse the wall's modular pieces scaled up, stacked to height, with an interior stair/ladder prop and an accessible top platform.
6. **Interior fit-out**: stalls, workbenches, and housing modules dropped into their rings once the shell is locked — these can iterate independently of the fortification geometry.
7. **Pass 2 — art/detail**: once the blockout plays correctly (traversal, sightlines, NPC pathing all work), swap primitives for higher-fidelity or custom assets and add set-dressing (banners, crates, awnings on stalls, guard details on towers).

## 5. Gameplay/NPC Hooks (ties to the main PRD)

- Merchant NPCs at stalls, artisan NPCs at inner-ring workbenches, and guard NPCs on the walls/towers are natural job-board roles under the town/org system — except here the "town" is the guild, not a settlement.
- The neutral-zone rule (no combat inside the walls) gives the org layer and dialogue system a clean stage to run scenes on without worrying about the survival-loop's threat systems interfering.
- This is a strong candidate for the first "always-on" LLM agent test bed: a bounded, self-contained location with clear roles (merchant, artisan, guard) is easier to validate the perceive→plan→act loop against than an open, sprawling town.

## 6. Concept Art Brief (for next pass)

**Framing**: aerial/establishing shot, showing the full ring from a 3/4 elevated angle so the moat, wall, gates, and towers, and the visible stall rooftops at the center all read at once.

**Mood**: functional medieval fortification with a market's warmth inside it — imagine a small castle bailey that's been repurposed for commerce. Weathered stone walls, practical (not ornate) towers, but colorful awnings/banners over the stalls visible over the wall line, smoke from forges in the inner ring.

**Key details to hit**:
- Visible concentric structure even from outside: wall silhouette, then a glimpse of taller stall roofs/smoke past the battlements.
- Bridges reading clearly as the only way in — moat should look genuinely uncrossable elsewhere.
- Towers taller than the wall, flanking each gate in a visibly matched pair.
- A sense of neutral/mercantile identity distinct from any one faction's heraldry — no single kingdom's banners, perhaps a guild mark/sigil instead on gate towers.

**Follow-up**: once the blockout is playable, an in-engine screenshot pass (from the actual s&box build) will likely be more useful for iterating than external concept art — recommend treating this brief as the pre-production mood-board step, then handing off to in-engine grey/white-box screenshots for the real iteration loop.
