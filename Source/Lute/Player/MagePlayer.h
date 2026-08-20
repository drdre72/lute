#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "Components/ActorComponent.h"
#include "MagePlayer.generated.h"

class USpellComponent;
class UInventoryComponent;
class USkeletalMeshComponent;
class UCameraComponent;
class USpringArmComponent;
class UStaticMeshComponent;

UCLASS()
class LUTE_API AMagePlayer : public ACharacter
{
    GENERATED_BODY()

public:
    AMagePlayer();

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Camera")
    USpringArmComponent* CameraBoom;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Camera")
    UCameraComponent* Camera;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Lute")
    USpellComponent* SpellComp;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Lute")
    UInventoryComponent* InventoryComp;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Mesh")
    UStaticMeshComponent* BodyMesh;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Lute")
    float CastRange;

protected:
    virtual void BeginPlay() override;
    virtual void SetupPlayerInputComponent(class UInputComponent* PlayerInputComponent) override;

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void CastAtCrosshair();

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void SwapTool();

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothing(int32 SlotIndex);

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothingHead() { RemoveClothing(0); }

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothingChest() { RemoveClothing(1); }

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothingLegs() { RemoveClothing(2); }

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothingFeet() { RemoveClothing(3); }

    UFUNCTION(BlueprintCallable, Category = "Lute")
    void RemoveClothingHands() { RemoveClothing(4); }

    // Movement
    void MoveForward(float Value);
    void MoveRight(float Value);
    void StartSprint();
    void StopSprint();

private:
    FVector GetLookTarget() const;
};
