#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

class FLuteRemoteControlModule : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;

private:
    void* HttpServerPtr = nullptr;
    bool bServerRunning = false;
};
