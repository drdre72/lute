#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "SpellType.generated.h"

UENUM(BlueprintType)
enum class ESpellType : uint8
{
    Fire        UMETA(DisplayName = "Fire"),        // place_prop, spawn_torch, spawn_campfire
    Ice         UMETA(DisplayName = "Ice"),         // sculpt_terrain raise/lower
    Earth       UMETA(DisplayName = "Earth"),       // sculpt_terrain flatten
    Wind        UMETA(DisplayName = "Wind"),        // sculpt_terrain smooth
    Lightning   UMETA(DisplayName = "Lightning"),   // scatter_on_terrain
    Heal        UMETA(DisplayName = "Heal"),        // default / utility
    Shadow      UMETA(DisplayName = "Shadow"),      // remove/delete
    Arcane      UMETA(DisplayName = "Arcane")       // building construction
};

UCLASS(BlueprintType)
class LUTE_API USpellMapping : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "Lute|Spells")
    static ESpellType GetSpellForTool(const FString& Action);

    UFUNCTION(BlueprintCallable, Category = "Lute|Spells")
    static FString GetIncantation(ESpellType Spell);

    UFUNCTION(BlueprintCallable, Category = "Lute|Spells")
    static FString GetAnimationState(ESpellType Spell);
};
