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
            "Networking",
            "HTTP",
            "Json",
            "JsonUtilities"
        });

        PrivateDependencyModuleNames.Add("WebSockets");
        PrivateDependencyModuleNames.Add("SSL");

        PrivateDefinitions.Add("WITH_AUTOMATION_TESTS=1");
        PrivateDefinitions.Add("WITH_DEV_AUTOMATION_TESTS=1");
        PrivateDefinitions.Add("WITH_AUTOMATION_WORKER=1");

        if (Target.Platform == UnrealTargetPlatform.Win64)
        {
            AddEngineThirdPartyPrivateStaticDependencies(Target, "OpenSSL");
            PrivateIncludePaths.Add(System.IO.Path.Combine(EngineDirectory, "Source/Runtime/Sockets/Private"));
        }
    }
}
