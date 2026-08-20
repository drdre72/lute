using UnrealBuildTool;

public class Lute : ModuleRules
{
    public Lute(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "InputCore",
            "GameplayTasks",
            "Niagara",       // VFX for spells
            "AnimGraphRuntime",
            "Slate",
            "SlateCore",
            "UMG",            // Inventory UI
            "HTTP",           // LLM API calls
            "Json",
            "JsonUtilities"
        });

        PrivateDependencyModuleNames.AddRange(new string[] { });

        // Uncomment for development builds
        // OptimizedCPPCode = true;
        // bUseAdaptiveUnityBuild = true;
    }
}
