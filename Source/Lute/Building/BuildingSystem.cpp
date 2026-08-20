#include "BuildingSystem.h"

void UBuildingSystem::BuildPiece(EBuildingPiece Piece, FVector Position, FRotator Rotation)
{
    FString PieceName = UEnum::GetValueAsString(Piece);
    UE_LOG(LogTemp, Log, TEXT("[Building] Placing %s at %s"), *PieceName, *Position.ToString());

    // TODO: Spawn actual mesh actor at position
    // SpawnPieceMesh(Piece, Position, Rotation);
}

FString UBuildingSystem::GetPieceModelPath(EBuildingPiece Piece)
{
    switch (Piece)
    {
    case EBuildingPiece::Foundation: return TEXT("/Game/Models/Building/foundation.fbx");
    case EBuildingPiece::Wall:       return TEXT("/Game/Models/Building/wall.fbx");
    case EBuildingPiece::Floor:      return TEXT("/Game/Models/Building/floor.fbx");
    case EBuildingPiece::Roof:       return TEXT("/Game/Models/Building/roof.fbx");
    case EBuildingPiece::Doorway:    return TEXT("/Game/Models/Building/doorway.fbx");
    case EBuildingPiece::Door:       return TEXT("/Game/Models/Building/door.fbx");
    case EBuildingPiece::Window:     return TEXT("/Game/Models/Building/window.fbx");
    case EBuildingPiece::Stairs:     return TEXT("/Game/Models/Building/stairs.fbx");
    case EBuildingPiece::Pillar:     return TEXT("/Game/Models/Building/pillar.fbx");
    case EBuildingPiece::Gate:       return TEXT("/Game/Models/Building/gate.fbx");
    default:                          return TEXT("");
    }
}
