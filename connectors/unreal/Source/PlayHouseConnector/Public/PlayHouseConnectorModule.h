#pragma once

#include "Modules/ModuleManager.h"

// PlayHouse Connector logging categories
DECLARE_LOG_CATEGORY_EXTERN(LogPlayHouse, Log, All);
DECLARE_LOG_CATEGORY_EXTERN(LogPlayHouseTransport, Log, All);

class FPlayHouseConnectorModule : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;
};
