#include "InventoryComponent.h"
#include "../Spells/SpellType.h"
#include "../Spells/SpellComponent.h"
#include "TimerManager.h"

UInventoryComponent::UInventoryComponent()
{
    ClothingSlots.SetNum(5); // Head, Chest, Legs, Feet, Hands
}

void UInventoryComponent::Equip(const FInventoryItem& Item)
{
    switch (Item.Type)
    {
    case EItemType::Weapon:
        EquippedWeapon = Item;
        OnItemEquipped.Broadcast(Item);
        UE_LOG(LogTemp, Log, TEXT("[Inventory] Equipped weapon: %s"), *Item.Name);
        break;

    case EItemType::Tool:
        EquippedTool = Item;
        // Update mage's spell type
        if (AActor* Owner = GetOwner())
        {
            if (UActorComponent* SpellComp = Owner->GetComponentByClass(USpellComponent::StaticClass()))
            {
                USpellComponent* SC = Cast<USpellComponent>(SpellComp);
                if (SC)
                {
                    SC->CurrentSpell = (ESpellType)Item.SpellType;
                }
            }
        }
        OnItemEquipped.Broadcast(Item);
        UE_LOG(LogTemp, Log, TEXT("[Inventory] Equipped tool: %s"), *Item.Name);
        break;

    case EItemType::Clothing:
        EquipClothing(Item);
        break;
    }
}

void UInventoryComponent::EquipClothing(const FInventoryItem& Item)
{
    int32 SlotIndex = (int32)Item.ClothingSlot;
    if (SlotIndex < 0 || SlotIndex >= ClothingSlots.Num()) return;

    // Play equip animation
    if (EquipMontage)
    {
        PlayMontage(EquipMontage);
    }

    PendingClothingSlot = SlotIndex;
    PendingClothingItem = Item;

    UE_LOG(LogTemp, Log, TEXT("[Inventory] Equipping %s to slot %d"), *Item.Name, SlotIndex);

    // Complete after animation
    GetWorld()->GetTimerManager().SetTimer(EquipTimerHandle, this, &UInventoryComponent::OnEquipComplete, 0.8f, false);
}

void UInventoryComponent::OnEquipComplete()
{
    if (PendingClothingSlot >= 0 && PendingClothingSlot < ClothingSlots.Num())
    {
        ClothingSlots[PendingClothingSlot] = PendingClothingItem;
        ToggleClothingMesh((EClothingSlot)PendingClothingSlot, true, PendingClothingItem.ModelPath);
        OnItemEquipped.Broadcast(PendingClothingItem);
        UE_LOG(LogTemp, Log, TEXT("[Inventory] Equipped %s to slot %d"), *PendingClothingItem.Name, PendingClothingSlot);
    }
    PendingClothingSlot = -1;
}

void UInventoryComponent::RemoveClothing(int32 SlotIndex)
{
    if (SlotIndex < 0 || SlotIndex >= ClothingSlots.Num()) return;
    if (ClothingSlots[SlotIndex].Id.IsEmpty()) return;

    // Play remove animation
    if (RemoveMontage)
    {
        PlayMontage(RemoveMontage);
    }

    PendingClothingSlot = SlotIndex;

    UE_LOG(LogTemp, Log, TEXT("[Inventory] Removing %s from slot %d"), *ClothingSlots[SlotIndex].Name, SlotIndex);

    GetWorld()->GetTimerManager().SetTimer(RemoveTimerHandle, this, &UInventoryComponent::OnRemoveComplete, 0.8f, false);
}

void UInventoryComponent::OnRemoveComplete()
{
    if (PendingClothingSlot >= 0 && PendingClothingSlot < ClothingSlots.Num())
    {
        FInventoryItem Removed = ClothingSlots[PendingClothingSlot];
        ToggleClothingMesh((EClothingSlot)PendingClothingSlot, false, TEXT(""));
        Items.Add(Removed);
        OnClothingRemoved.Broadcast((EClothingSlot)PendingClothingSlot);
        UE_LOG(LogTemp, Log, TEXT("[Inventory] Removed %s from slot %d"), *Removed.Name, PendingClothingSlot);
        ClothingSlots[PendingClothingSlot] = FInventoryItem();
    }
    PendingClothingSlot = -1;
}

void UInventoryComponent::SwapTool()
{
    // Collect all weapons and tools
    TArray<int32> ToolIndices;
    for (int32 i = 0; i < Items.Num(); i++)
    {
        if (Items[i].Type == EItemType::Tool || Items[i].Type == EItemType::Weapon)
        {
            ToolIndices.Add(i);
        }
    }

    if (ToolIndices.Num() == 0) return;

    // Find current equipped index
    int32 CurrentIdx = -1;
    for (int32 i = 0; i < ToolIndices.Num(); i++)
    {
        if (Items[ToolIndices[i]].Id == EquippedTool.Id || Items[ToolIndices[i]].Id == EquippedWeapon.Id)
        {
            CurrentIdx = i;
            break;
        }
    }

    int32 NextIdx = (CurrentIdx + 1) % ToolIndices.Num();
    Equip(Items[ToolIndices[NextIdx]]);
}
