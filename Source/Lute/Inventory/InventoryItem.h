#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "InventoryItem.generated.h"

UENUM(BlueprintType)
enum class EItemType : uint8
{
    Weapon,
    Tool,
    Clothing,
    Consumable,
    Material,
    Building
};

UENUM(BlueprintType)
enum class EClothingSlot : uint8
{
    Head,
    Chest,
    Legs,
    Feet,
    Hands,
    None
};

USTRUCT(BlueprintType)
struct LUTE_API FInventoryItem
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    FString Id;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    FString Name;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    EItemType Type;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    FString IconPath;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    FString ModelPath;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    int32 StackSize = 1;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    int32 Count = 1;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    EClothingSlot ClothingSlot = EClothingSlot::None;

    // Associated spell type for tools/weapons
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
    uint8 SpellType = 0; // ESpellType cast to uint8
};
