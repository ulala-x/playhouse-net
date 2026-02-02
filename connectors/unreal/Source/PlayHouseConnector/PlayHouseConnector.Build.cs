using UnrealBuildTool;

public class PlayHouseConnector : ModuleRules
{
    public PlayHouseConnector(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine"
        });

        PrivateDependencyModuleNames.AddRange(new string[]
        {
            "Sockets",
            "Networking"
        });

        PrivateDependencyModuleNames.Add("WebSockets");
        PrivateDependencyModuleNames.Add("SSL");
    }
}
