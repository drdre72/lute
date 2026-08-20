#include "SpellComponent.h"

USpellComponent::USpellComponent()
{
    CurrentSpell = ESpellType::Fire;
    bIsCasting = false;
}

bool USpellComponent::CastSpell(FVector TargetPosition)
{
    if (bIsCasting) return false;

    bIsCasting = true;

    // Face the target
    AActor* Owner = GetOwner();
    if (Owner)
    {
        FVector Dir = (TargetPosition - Owner->GetActorLocation());
        Dir.Z = 0;
        Dir.Normalize();
        if (!Dir.IsNearlyZero())
        {
            FRotator TargetRot = Dir.Rotation();
            Owner->SetActorRotation(TargetRot);
        }
    }

    UE_LOG(LogTemp, Log, TEXT("[SpellComponent] Casting %s at %s"),
        *USpellMapping::GetIncantation(CurrentSpell),
        *TargetPosition.ToString());

    // Play animation
    PlayCastAnimation(CurrentSpell);

    // Spawn VFX
    SpawnSpellVFX(CurrentSpell, TargetPosition);

    // Play TTS
    PlayIncantation(CurrentSpell);

    // Broadcast event
    OnSpellCast.Broadcast(CurrentSpell, TargetPosition);

    // Set completion timer (1.5s cast time)
    GetWorld()->GetTimerManager().SetTimer(CastTimerHandle, this, &USpellComponent::OnCastComplete, 1.5f, false);

    return true;
}

void USpellComponent::OnCastComplete()
{
    bIsCasting = false;
    OnSpellComplete.Broadcast(CurrentSpell, FVector::ZeroVector);
    UE_LOG(LogTemp, Log, TEXT("[SpellComponent] Cast complete"));
}
