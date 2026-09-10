# Product Requirement Document (PRD)

This file is read by the AI agent (`agent/agent.py`) at the start of every
session (single-task or interactive chat) so it has persistent knowledge of
what the project is and what you want built. Edit this freely; the agent only
reads it, it never writes to it.

## Project Overview

- **Project Name:** Lute
- **Tagline:** High-Stakes Persistent Sandbox & Soul Retrieval MMORPG
- **Engine / Tech Stack:** S&Box (Source 2), C# (.NET 10 / LangVersion 14), `Sandbox` namespace, Razor HUD components, SQLite / PostgreSQL master database
- **Target Hosting:** Small-scale VPS ($20-$40/mo per shard, 50-100 simultaneous players)
- **Core Philosophy:** Deep specialization, zero-RNG deterministic growth, player-driven governance, and a cross-realm soul retrieval loop connecting persistent and seasonal servers.

## 1. World & Architecture Concept

### 1.1 Dual-Realm Structure & Soul Retrieval

- **Permanent Realm (The Anchor):** A persistent, high-stakes world with zero scheduled wipes. Dying locks your character's soul.
- **Seasonal Realm (The Crucible):** Server shards running on fixed wipe schedules (1-month, 3-month, 6-month, or 1-year cycles).
- **The Time Portal:** The lore-friendly spawn mechanism. Every character or resurrected soul materializes into a server world via an in-world Time Portal with a brief temporal invulnerability buffer.
- **Soul-Locked State:**
  - Dying in the Permanent Realm freezes the character in a void/purgatory state (unplayable, non-respawnable).
  - **Retrieval Loop:** To resurrect a locked character, the player must participate in a Seasonal Realm. If their guild achieves seasonal victory (holding the Crown, max Majesty, or surviving the End-of-Days), all guild members' dead Permanent Realm souls are restored (`soul_status = 'RESTORED'`).
  - **Soul Resonance Bonus:** Completing a Seasonal Realm with an intact soul (no dead Permanent character) grants permanent cosmetic aura effects, dimensional storage, and a minor reduction in opposite-node attribute penalties (capped at 5% total).

### 1.2 Monument Theme — Famous & Mythical Places in Their Prime

- **Concept:** Every major monument in the world is a real-world (or mythical) famous place depicted in its **prime condition** — not as ruins, but as it looked at its peak. The Time Portal pulled these places from across history into the Lute world.
- **Easter Egg Layer:** Players who recognize the real-world site get a reward of recognition; those who don't just see impressive architecture. No in-game text labels them — discovery is organic.
- **Examples:**
  - **St. Peter's Basilica** (archway — first imported asset, currently the Neutral Market monument's gate)
  - **The Acropolis / Parthenon** (in prime condition)
  - **Santa Maria** (shipwrecked but pristine — a ship monument)
  - **The Sphinx** (original face, or a variant with Napoleon's head as a joke)
  - **Library of Alexandria** (lost to history, prime condition)
  - **Hanging Gardens of Babylon**
  - **Colossus of Rhodes** (intact, straddling a harbor)
  - **Tower of Babel** (under eternal construction — see §1.3)
  - **Atlantis** (underwater or risen)
  - **Troy** (walls intact, Trojan Horse as a landmark)
  - **Stonehenge** (complete, not ruined)
  - **Gobekli Tepe** (obscure but mysterious — oldest known temple)
  - **Petra** (facade in full color, not weathered)
- **Obscure/Mysterious Priority:** Lesser-known but mysterious sites (churches, temples, ruins) are preferred over obvious tourist landmarks — the goal is wonder and discovery, not a checklist.

### 1.3 Tower of Babel Monument — "Babblespeak" Mechanic

- **Concept:** The Tower of Babel monument is under eternal, never-completing construction. The NPCs working on it are stuck in an infinite loop because they cannot communicate effectively — a direct parallel to the biblical curse.
- **Babblespeak:** NPCs at the tower speak in overly complex syntax and vocabulary — sentences that are technically correct English but nearly impossible to parse for an average reader. Not a foreign language or cipher, but *your own language made incomprehensible through excessive complexity*. May also incorporate actual other languages (or a mix of languages and complex English) — the biblical curse scattered their tongues, so the babblespeak could be a blend of excessive syntactic complexity AND foreign-language fragments.
- **Dictionary Pages:** Collectible pages scattered around the world at other monuments, ruins, and POIs. Each page unlocks simpler synonyms or common-syntax equivalents for specific words/phrases.
- **Progressive Translation:** Without dictionary pages, NPC dialogue options only show the words/phrases the player has already "translated" — the rest appears as garbled or overly complex text. As more pages are collected, NPC dialogue progressively becomes readable.
- **Soft Gate, Not Hard Gate:** Players *can* brute-force through dialogue with partial understanding, but the payoff scales with comprehension:
  - **Recruitment:** The goal is to recruit tower NPCs as workers for your guild (continuous workers you can hire).
  - **Comprehension Multiplier:** If the player has enough dictionary pages to translate ~80% of the "highlighted ridiculous words," the NPC's starting relationship is higher and their skill growth rate over time gets a higher multiplier.
  - **Guessing Through:** With enough pages you can probably guess through the rest, but the relationship and rates suffer.
- **Dictionary Pages as Double-Duty Collectibles:**
  - **Lore:** Each page contains etymology, word origins, and ties to the world's history.
  - **Progression Gate:** Certain monuments/quests require enough vocabulary to navigate NPC dialogue.
  - **Exploration Driver:** Pages are placed at other monuments, ruins, and POIs, encouraging world traversal.

## 2. Character Progression & Attributes

### 2.1 Polar-Opposite Attribute Web (9 Nodes / 3 Clusters)

Attribute values exist on a flexible 9-axis vector circle. Investing in one node stretches it into an outward spike, while pulling its polar opposite 180 degrees across the wheel inward past baseline as an active penalty.

```
				  [Health] (Physical)
			/              \
	[Perception]          [Constitution]
		/                      \
  [Intelligence]              [Honor] (No Penalty Node)
	   |                          |
	[Mana]                    [Stamina]
		\                      /
	 [Coordination]        [Agility]
```

- **Physical Trinity:** Health <-> Coordination | Constitution <-> Mana | Stamina <-> Perception
- **Spiritual/Control Trinity:** Agility <-> Health | Honor (No Penalty) | Intelligence <-> Opposite Nodes
- **Distance Strain:** Training past 70% of an attribute costs exponentially more effort and doubles the inward contraction rate of its opposite node.

### 2.2 Polar-Opposite Skill Web (9 Specialization Lines)

Operates identically to the attribute web, layering over character attributes:

- Heavy Weaponry <-> Conjuration (Summoned entities collapse or turn hostile if Heavy Weaponry is spiked)
- Protection <-> Survival
- Tradecraft <-> Destruction
- Athletics <-> Restoration
- Subterfuge <-> Heavy Weaponry

### 2.3 Deterministic Progression & Profession Quests (Zero RNG)

- **Skill-Driven Progression:** Primary skill growth occurs organically through direct usage (e.g., crafting increases Tradecraft, taking hits increases Protection).
- **Profession Archetype Quests:** Profession-based NPCs generate dynamic tasks aligned with their labor discipline:
  - **Blacksmiths / Artisans:** Request raw ores, fuel, or weapon crafting, granting targeted Tradecraft and Constitution progression upon completion.
  - **Captains / Guards:** Assign patrol routes, beast clearing, or bounty hunts, yielding direct Heavy Weaponry, Protection, or Stamina gains.
  - **Scholars / Alchemists:** Assign reagent gathering or ritual assistance, awarding Mana, Intelligence, or Restoration experience.
- **Guild & Macro Economic Integration:** Quest generation scales dynamically based on local guild and market shortages. If a ruling guild's treasury or crafting depots lack timber, local woodcutters and carpenter NPCs spawn high-priority gathering tasks to fulfill the economic deficit.
- **Mastery Threshold:** Level 100 Mastery requires a context-specific trial (class/race/time/guild requirements) before unlocking slow, uncapped, diminishing-return endgame growth.

## 3. UI, Immersion, & Wayfinding

### 3.1 Persistent Journal Snippet

- A lightweight, non-intrusive HUD widget (Razor `HUDComponent`) that displays short text entries ("Follow the eastern river past the split oak").
- No map markers, objective distance numbers, or compass floating arrows.

### 3.2 Archetypal NPC Directions & World Signage

- **Guards:** Grid-based, landmark, and military distance directions.
- **Peasants/Locals:** Folkloric, environmental, and sensory landmarks.
- **Merchants:** Trade routes, crossroads, and geographical features.
- Physical in-world road signs, carved stones, and guild emblems handle navigation.

## 4. Governance & Kingdom Mechanics

### 4.1 Majesty Threshold & Self-Declaration

A guild transforms into a Crown Entity by hitting a Majesty threshold covering broad societal metrics: Infrastructure, Economic Output, Territorial Safety, and Civic Diversity.

### 4.2 Monarchy vs. Council

- **Monarchy:** Grants boosts to Army Recruitment speed, Resource Gathering yields, and State Religion/Ideology buffs.
- **Council:** Grants boosts to Cultural Prestige, Regional Trade/Diplomacy tariffs, and unlocks a voting/legislative system for guild leaders.

### 4.3 Territorial Control & Taxation

- Jurisdiction extends only as far as physical watchtowers and patrols are maintained.
- Rulers set custom tariffs (Market Taxes, Property Fees, Highway Tolls). Excessive tax rates degrade regional economics and drive players away.

## 5. Server Architecture & Technical Scope

### 5.1 S&Box Server Setup

- **Execution:** S&Box dedicated server running on Linux VPS via the `sbox-server` host. Server-only code lives in the same `code/` assembly and is gated with `Host.IsServer` / `[GameLayer.Server]` checks.
- **Networking:** S&Box's built-in client/server networking model. State that must replicate is exposed via `[Sync]` properties and `Component` fields; one-off events use `[Broadcast]` RPCs. Spatial interest management is handled by the engine's networking layer (per-player visibility/culling).
- **Authority:** Server is authoritative over world state, soul status, economy, and quest generation. Client code is limited to input, prediction, and HUD rendering.
- **Persistence:** SQLite for local shard state; HTTPS API to central Master DB for account authentication and cross-realm `soul_status` sync. DB access runs server-side only.

### 5.2 Dynamic Quest Generation Engine

- **Economic Watcher:** A server-side `Component` (ticked on `OnFixedUpdate` or a throttled timer) monitors regional marketplace stocks and guild storage levels.
- **Archetype Factory:** When supply thresholds drop below set limits, relevant NPC archetypes auto-generate local supply and service quests directly linked to target skill rewards.

### 5.3 Server Wipe Lifecycle

- **Seasonal End-of-Days:** The final 2 weeks of a Seasonal Realm trigger escalating beast attacks and environmental hazards, culminating in a final throne defense battle.
- **Wipe Execution:** Automated database reset clearing world geometry, local stats, and inventory while preserving earned Legacy Tokens and updating Master DB soul states.

## Current goals

<!-- What should the agent focus on right now? Bullet list works well. -->

## Constraints / conventions

- Engine: S&Box (Source 2), C# .NET 10, `Sandbox` namespace, `RootNamespace=Sandbox`.
- All gameplay code is C# in `sbox/code/`, built via `sbox/code/lute.csproj` (Razor SDK, `SANDBOX` define). Do not mix Godot/GDScript or Unreal C++ into this project.
- Use the S&Box component model: gameplay entities are `GameObject`s with `Component` derivatives (`OnStart`, `OnUpdate`, `OnFixedUpdate`, `OnDestroy`). Prefer `[Property]`, `[RequireComponent]`, `[Sync]`, `[Broadcast]` over hand-rolled networking.
- Movement/look uses the built-in `PlayerController`; extend behavior via a sibling `Component` (see `LutePlayer.cs`) rather than replacing it.
- Materials are `.vmat_c`, textures `.vtex_c`, models `.vmdl_c` — Source 2 asset formats under `sbox/.sbox/cloud/` and `sbox/Assets/`. Do not reference Godot `.tres`/`.tscn`/`.import` resources.
- HUD/UI is built with Razor `HUDComponent` / `Panel` classes (the csproj uses `Microsoft.NET.Sdk.Razor`), not Godot Control nodes.
- Input bindings live in `sbox/ProjectSettings/Input.config`; reference actions by name via `Input.Pressed("...")` / `Input.Down("...")`.
- Prefer `Log.Info`/`Log.Warning` for diagnostics (S&Box logging, not GDScript `print`).
- <!-- Add naming conventions, folder structure rules, coding style, etc. -->

## Non-goals

<!-- Explicitly out of scope, to keep the agent from wandering. -->
