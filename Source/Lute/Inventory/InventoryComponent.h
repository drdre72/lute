#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "InventoryItem.h"
#include "InventoryComponent.generated.h"

class USkeletalMeshComponent;
class UAnimMontage;

UCLASS(ClassGroup=(Lute), meta=(BlueprintSpawnableComponent))
class LUTE_API UInventoryComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    UInventoryComponent();

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Inventory")
    TArray<FInventoryItem> Items;

    UPROPERTY(BlueprintReadOnly, Category = "Inventory")
    FInventoryItem EquippedWeapon;

    UPROPERTY(BlueprintReadOnly, Category = "Inventory")
    FInventoryItem EquippedTool;

    // 5 clothing slots: Head, Chest, Legs, Feet, Hands
    UPROPERTY(BlueprintReadOnly, Category = "Inventory")
    TArray<FInventoryItem> ClothingSlots;

    // Montages for equip/remove animations
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Inventory|Animation")
    UAnimMontage* EquipMontage;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Inventory|Animation")
    UAnimMontage* RemoveMontage;

    UFUNCTION(BlueprintCallable, Category = "Lute|Inventory")
    void Equip(const FInventoryItem& Item);

    UFUNCTION(BlueprintCallable, Category = "Lute|Inventory")
    void EquipClothing(const FInventoryItem& Item);

    UFUNCTION(BlueprintCallable, Category = "Lute|Inventory")
    void RemoveClothing(int32 SlotIndex);

    UFUNCTION(BlueprintCallable, Category = "Lute|Inventory")
    void SwapTool();

    // Blueprint-implemented — play montage on the mage's mesh
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Inventory")
    void PlayMontage(UAnimMontage* Montage);

    // Blueprint-implemented — toggle clothing mesh visibility
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Inventory")
    void ToggleClothingMesh(EClothingSlot Slot, bool bVisible, const FString& ModelPath);

    DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnItemEquipped, FInventoryItem, Item);
    DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnClothingRemoved, EClothingSlot, Slot);

    UPROPERTY(BlueprintAssignable, Category = "Lute|Inventory")
    FOnItemEquipped OnItemEquipped;

    UPROPERTY(BlueprintAssignable, Category = "Lute|Inventory")
    FOnClothingRemoved OnClothingRemoved;

private:
    FTimerHandle EquipTimerHandle;
    FTimerHandle RemoveTimerHandle;

    void OnEquipComplete();
    void OnRemoveComplete();

    int32 PendingClothingSlot = -1;
    FInventoryItem PendingClothingItem;
};
