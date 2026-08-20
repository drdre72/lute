# AGENT_RULES.md — Project Lute (Godot 4.x)

This file is read by the AI agent (`agent/agent.py`) at the start of every
session, alongside `PRD.md` and `PROGRESS_LOG.md`. Edit this freely; the
agent only reads it, it never writes to it.

## Core Context & Architectural Rules

You are an expert GDScript / C# game developer working on **Lute**, a
high-stakes persistent sandbox MMORPG built in Godot 4.x.

### Engine & Code Constraints

- **Target Engine:** Godot 4.x (use Godot 4 syntax only; e.g., `@export`, `Callable`, `Vector2i`, `super()`).
- **Networking:** Headless Linux dedicated server using `ENetMultiplayerPeer` and spatial hashing.
- **Data Architecture:** Local SQLite database for world shard state; HTTPS API to Central Master DB for character `soul_status`.
- **Code Style:** Clean, highly modular GDScript. Prefer static typing (`var hp: int = 100`) to help local models maintain type safety.

---

## Game Systems Architecture Reference

### 1. Dual Polar-Opposite Stat Webs (Radial Mechanics)

- Stats exist on a 9-axis vector circle. Investing in one stat creates an outward "spike" while pulling its polar opposite 180 degrees across the wheel inward as a penalty.
- **Attribute Pairs:**
  - `Health` <-> `Coordination`
  - `Constitution` <-> `Mana`
  - `Stamina` <-> `Perception`
  - `Agility` <-> `Health`
  - `Intelligence` <-> Opposite Nodes
  - `Honor` (Special Node: applies **no** opposite penalty).
- **Skill Pairs:**
  - `Heavy Weaponry` <-> `Conjuration` (spiking Heavy Weaponry causes summoned entities to collapse or turn hostile).
  - `Protection` <-> `Survival`
  - `Tradecraft` <-> `Destruction`
  - `Athletics` <-> `Restoration`
  - `Subterfuge` <-> `Heavy Weaponry`

### 2. Immersion & Navigation

- **NO** floating LFG markers, minimap icons, or objective arrows on screen.
- Wayfinding uses a persistent text journal snippet and in-world physical signage.
- NPC direction generation uses archetype filtering (Guards = grid/landmarks; Peasants = folklore/landmarks; Merchants = routes/crossroads).

### 3. Progression & Economic Quest Engine

- **Zero-RNG Progression:** Skills improve directly through usage or completing profession tasks.
- **Dynamic Quests:** An asynchronous thread monitors regional database shortages. When resource thresholds drop, local profession NPCs generate supply tasks rewarding targeted skills/attributes.

### 4. Kingdom Governance & Wipe Mechanics

- Guilds unlock Crown status via **Majesty Rating** (Infrastructure, Economy, Safety, Civic Diversity).
- Governance routes: **Monarchy** (Recruitment, Resource Yields, Religion) vs. **Council** (Trade, Diplomacy, Voting).
- **Soul Retrieval Loop:** Dying in the Permanent Realm sets `soul_status = 'LOCKED'`. Souls are redeemed (`'RESTORED'`) when the player's guild achieves victory in a Seasonal Wipe Realm.

---

## Developer Instructions for Qwen

1. **Be Concise & Direct:** Output functional GDScript/C# code immediately without unnecessary introductory preamble.
2. **Type Safety:** Always type variables and function return signatures explicitly.
3. **Godot 4 Syntax:** Never use Godot 3 syntax (e.g., use `@onready` instead of `onready`, use `connect("signal", callable)` instead of string-signal connections).
4. **Error Handling:** Include basic checks for `null` nodes, valid array bounds, and async database connections.
