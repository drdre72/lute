#include "Lute.h"
#include "Modules/ModuleManager.h"

IMPLEMENT_MODULE(FLuteModule, Lute)

void FLuteModule::StartupModule()
{
    UE_LOG(LogTemp, Log, TEXT("[Lute] Module started"));
}

void FLuteModule::ShutdownModule()
{
    UE_LOG(LogTemp, Log, TEXT("[Lute] Module shutdown"));
}
