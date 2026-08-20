# Product Requirement Document (PRD)

This file is read by the AI agent (`agent/agent.py`) at the start of every
session (single-task or interactive chat) so it has persistent knowledge of
what the project is and what you want built. Edit this freely; the agent only
reads it, it never writes to it.

## Project Overview

- **Project Name:** Lute
- **Tagline:** High-Stakes Persistent Sandbox & Soul Retrieval MMORPG
- **Engine / Tech Stack:** Godot 4.x (Headless Linux Dedicated Server), GDScript / C#, SQLite / PostgreSQL master database
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

- A lightweight, non-intrusive UI widget that displays short text entries ("Follow the eastern river past the split oak").
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

### 5.1 Godot 4 Server Setup

- **Execution:** Godot Headless Export running on Linux VPS (under 500 MB RAM base).
- **Networking:** `ENetMultiplayerPeer` handling spatial hashing (interest management) to broadcast entity updates only to clients within visual range.
- **Persistence:** SQLite for local shard state; HTTPS API to central Master DB for account authentication and cross-realm `soul_status` sync.

### 5.2 Dynamic Quest Generation Engine

- **Economic Watcher:** An asynchronous server thread monitors regional marketplace stocks and guild storage levels.
- **Archetype Factory:** When supply thresholds drop below set limits, relevant NPC archetypes auto-generate local supply and service quests directly linked to target skill rewards.

### 5.3 Server Wipe Lifecycle

- **Seasonal End-of-Days:** The final 2 weeks of a Seasonal Realm trigger escalating beast attacks and environmental hazards, culminating in a final throne defense battle.
- **Wipe Execution:** Automated database reset clearing world geometry, local stats, and inventory while preserving earned Legacy Tokens and updating Master DB soul states.

## Current goals

<!-- What should the agent focus on right now? Bullet list works well. -->

## Constraints / conventions

- Engine: Godot 4.7, C#/.NET (`project/assembly_name="Lute"`), Jolt Physics, Forward+ renderer.
- Prefer `res://` paths for all scene/script/resource references.
- <!-- Add naming conventions, folder structure rules, coding style, etc. -->

## Non-goals

<!-- Explicitly out of scope, to keep the agent from wandering. -->
