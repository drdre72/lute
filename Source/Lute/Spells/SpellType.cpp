#include "SpellType.h"

ESpellType USpellMapping::GetSpellForTool(const FString& Action)
{
    if (Action == "place_prop" || Action == "spawn_torch" || Action == "spawn_campfire")
        return ESpellType::Fire;
    if (Action == "sculpt_terrain")
    {
        if (Action.Contains("raise") || Action.Contains("lower"))
            return ESpellType::Ice;
        if (Action.Contains("flatten"))
            return ESpellType::Earth;
        if (Action.Contains("smooth"))
            return ESpellType::Wind;
    }
    if (Action == "scatter_on_terrain")
        return ESpellType::Lightning;
    if (Action == "build_structure")
        return ESpellType::Arcane;
    if (Action == "remove" || Action == "delete")
        return ESpellType::Shadow;
    return ESpellType::Heal;
}

FString USpellMapping::GetIncantation(ESpellType Spell)
{
    switch (Spell)
    {
    case ESpellType::Fire:      return TEXT("Ignis!");
    case ESpellType::Ice:       return TEXT("Glacies!");
    case ESpellType::Lightning: return TEXT("Fulmen!");
    case ESpellType::Earth:     return TEXT("Terra!");
    case ESpellType::Wind:      return TEXT("Ventus!");
    case ESpellType::Heal:      return TEXT("Sancto!");
    case ESpellType::Shadow:    return TEXT("Umbra!");
    case ESpellType::Arcane:    return TEXT("Aedifico!");
    default:                    return TEXT("Magica!");
    }
}

FString USpellMapping::GetAnimationState(ESpellType Spell)
{
    switch (Spell)
    {
    case ESpellType::Fire:      return TEXT("cast_fire");
    case ESpellType::Ice:       return TEXT("cast_ice");
    case ESpellType::Lightning: return TEXT("cast_lightning");
    case ESpellType::Earth:     return TEXT("cast_earth");
    case ESpellType::Wind:      return TEXT("cast_wind");
    case ESpellType::Heal:      return TEXT("cast_heal");
    case ESpellType::Shadow:    return TEXT("cast_shadow");
    case ESpellType::Arcane:    return TEXT("cast_arcane");
    default:                    return TEXT("cast_default");
    }
}
