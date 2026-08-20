#include "SpellTTS.h"
#include "Kismet/GameplayStatics.h"

void USpellTTS::PlayIncantation(ESpellType Spell, FVector Position)
{
    FString Incantation = USpellMapping::GetIncantation(Spell);
    FString SoundName = GetSoundName(Spell);

    UE_LOG(LogTemp, Log, TEXT("[TTS] %s (%s) at %s"), *Incantation, *SoundName, *Position.ToString());

    // TODO: Load and play actual sound files
    // USoundBase* Sound = LoadObject<USoundBase>(nullptr, *FString::Printf(TEXT("/Game/Sounds/%s.%s"), *SoundName, *SoundName));
    // if (Sound)
    // {
    //     UGameplayStatics::PlaySoundAtLocation(GetWorld(), Sound, Position);
    // }
}

FString USpellTTS::GetSoundName(ESpellType Spell)
{
    switch (Spell)
    {
    case ESpellType::Fire:      return TEXT("spell_fire");
    case ESpellType::Ice:       return TEXT("spell_ice");
    case ESpellType::Lightning: return TEXT("spell_lightning");
    case ESpellType::Earth:     return TEXT("spell_earth");
    case ESpellType::Wind:      return TEXT("spell_wind");
    case ESpellType::Heal:      return TEXT("spell_heal");
    case ESpellType::Shadow:    return TEXT("spell_shadow");
    case ESpellType::Arcane:    return TEXT("spell_arcane");
    default:                    return TEXT("spell_default");
    }
}
