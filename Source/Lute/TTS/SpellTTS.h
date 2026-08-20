#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "../Spells/SpellType.h"
#include "SpellTTS.generated.h"

class USoundBase;

UCLASS(BlueprintType)
class LUTE_API USpellTTS : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    // Play a TTS incantation for the given spell type at a position
    UFUNCTION(BlueprintCallable, Category = "Lute|TTS")
    static void PlayIncantation(ESpellType Spell, FVector Position);

    // Get the sound asset name for a spell type
    UFUNCTION(BlueprintPure, Category = "Lute|TTS")
    static FString GetSoundName(ESpellType Spell);
};
