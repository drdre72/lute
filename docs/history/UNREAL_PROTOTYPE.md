# Historical Phase: Unreal Engine 5 Prototype

This document preserves the history of Lute's Unreal Engine 5 prototype phase. This code is no longer part of the project — Lute has migrated to S&Box (Source 2) with C#. This file exists for historical reference only.

## Original Description

Lute was originally prototyped as a medieval building game powered by LLM-driven mage spells, TTS incantations, and an inventory system, built on Unreal Engine 5.

## Features (Historical)

- **Mage Player Controller** — movement, spell casting, animation states
- **Spell System** — 8 spell types mapped to tools, each with unique VFX + animation
- **TTS Incantations** — voice playback per spell type
- **Inventory System** — weapons, tools, clothing with equip/remove animations
- **Building System** — medieval building pieces (walls, foundations, roofs)
- **LLM Agent** — chat commands trigger the mage to build via LLM API

## Project Structure (Historical)

```
lute-unreal/
├── Lute.uproject              # Unreal project manifest
├── Source/
│   └── Lute/
│       ├── Lute.Build.cs      # Module build config
│       ├── Lute.h / .cpp      # Game module entry
│       ├── Core/
│       │   └── LuteGameManager.h/.cpp
│       ├── Player/
│       │   ├── MagePlayer.h/.cpp
│       │   └── MagePlayerController.h/.cpp
│       ├── Spells/
│       │   ├── SpellType.h
│       │   └── SpellComponent.h/.cpp
│       ├── Inventory/
│       │   ├── InventoryItem.h
│       │   └── InventoryComponent.h/.cpp
│       ├── TTS/
│       │   └── SpellTTS.h/.cpp
│       ├── LLM/
│       │   └── LLMAgent.h/.cpp
│       └── Building/
│           └── BuildingSystem.h/.cpp
├── Content/
│   ├── Blueprints/             # Animation blueprints, UI widgets
│   ├── Models/                 # 3D models (.fbx, .obj)
│   ├── Materials/              # Materials and textures
│   ├── Sounds/                 # Spell voice lines
│   └── Animations/             # Cast animations, equip/remove
└── Config/
    └── DefaultEngine.ini       # Engine configuration
```

## Spell → Tool Mapping (Historical)

| Tool Action | Spell Type | Incantation | Animation |
|---|---|---|---|
| place_prop, spawn_torch | Fire | "Ignis!" | cast_fire |
| sculpt_terrain (raise/lower) | Ice | "Glacies!" | cast_ice |
| sculpt_terrain (flatten) | Earth | "Terra!" | cast_earth |
| sculpt_terrain (smooth) | Wind | "Ventus!" | cast_wind |
| scatter_on_terrain | Lightning | "Fulmen!" | cast_lightning |
| build_structure | Arcane | "Aedifico!" | cast_arcane |
| remove/delete | Shadow | "Umbra!" | cast_shadow |
| default/utility | Heal | "Sancto!" | cast_heal |

## Why the Migration

The project migrated to S&Box (Source 2) for:
- C# gameplay code (vs Unreal C++)
- Built-in MCP server for AI agent inspection
- Source 2 terrain and component system
- Better fit for the procedural construction pipeline

See `README.md` for the current project state.
