#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "LuteGameManager.generated.h"

UCLASS()
class LUTE_API ALuteGameManager : public AGameModeBase
{
    GENERATED_BODY()

public:
    ALuteGameManager();

protected:
    virtual void BeginPlay() override;
};
