#pragma once

#include "Modules/ModuleManager.h"

class FPlayHouseConnectorModule : public IModuleInterface
{
public:
    virtual void StartupModule() override;
    virtual void ShutdownModule() override;
};
