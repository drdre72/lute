#pragma once

#include "CoreMinimal.h"
#include "UObject/NoExportTypes.h"
#include "BuildingSystem.generated.h"

UENUM(BlueprintType)
enum class EBuildingPiece : uint8
{
    Foundation,
    Wall,
    Floor,
    Roof,
    Doorway,
    Door,
    Window,
    Stairs,
    Pillar,
    Gate
};

UCLASS(BlueprintType, Blueprintable)
class LUTE_API UBuildingSystem : public UObject
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "Lute|Building")
    static void BuildPiece(EBuildingPiece Piece, FVector Position, FRotator Rotation);

    UFUNCTION(BlueprintCallable, Category = "Lute|Building")
    static FString GetPieceModelPath(EBuildingPiece Piece);

    // Blueprint-implemented — spawn the actual mesh
    UFUNCTION(BlueprintImplementableEvent, Category = "Lute|Building")
    void SpawnPieceMesh(EBuildingPiece Piece, FVector Position, FRotator Rotation);
};
