#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "SpellType.h"
#include "SpellComponent.generated.h"

class UNiagaraSystem;
class USoundBase;

UCLASS(ClassGroup=(Lute), meta=(BlueprintSpawnableComponent))
class LUTE_API USpellComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    USpellComponent();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spells")
    ESpellType CurrentSpell;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spells")
    bool bIsCasting;

    // VFX per spell type (set in Blueprint or editor)
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spells|VFX")
    TMap<ESpellType, UNiagaraSystem*> SpellVFX;

    // Sound per spell type
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Spells|Sound")
    TMap<ESpellType, USoundBase*> SpellSounds;

    // Cast a spell at target position. Returns true if cast started.
    UFUNCTION(BlueprintCallable, Category = "Lute|Spells")
    bool CastSpell(FVector TargetPosition);

    // Blueprint-implemented — play the cast animation montage
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Spells")
    void PlayCastAnimation(ESpellType SpellType);

    // Blueprint-implemented — spawn VFX at target
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Spells")
    void SpawnSpellVFX(ESpellType SpellType, FVector TargetPosition);

    // Blueprint-implemented — play TTS sound
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Spells")
    void PlayIncantation(ESpellType SpellType);

    DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnSpellCast, ESpellType, SpellType, FVector, TargetPosition);

    UPROPERTY(BlueprintAssignable, Category = "Lute|Spells")
    FOnSpellCast OnSpellCast;

    UPROPERTY(BlueprintAssignable, Category = "Lute|Spells")
    FOnSpellCast OnSpellComplete;

private:
    FTimerHandle CastTimerHandle;

    void OnCastComplete();
};
