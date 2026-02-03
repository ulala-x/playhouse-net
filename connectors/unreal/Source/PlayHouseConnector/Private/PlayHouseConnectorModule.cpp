#include "PlayHouseConnectorModule.h"
#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

IMPLEMENT_MODULE(FPlayHouseConnectorModule, PlayHouseConnector)

void FPlayHouseConnectorModule::StartupModule()
{
    UE_LOG(LogTemp, Log, TEXT("PlayHouseConnector module loaded (WITH_AUTOMATION_TESTS=%d)"), WITH_AUTOMATION_TESTS);
}

void FPlayHouseConnectorModule::ShutdownModule() {}
