#include "LuteGameManager.h"

ALuteGameManager::ALuteGameManager()
{
    DefaultPawnClass = nullptr; // Set to AMagePlayer in Blueprint
    PlayerControllerClass = nullptr; // Set in Blueprint
}

void ALuteGameManager::BeginPlay()
{
    Super::BeginPlay();
    UE_LOG(LogTemp, Log, TEXT("[Lute] Game manager initialized"));
}
