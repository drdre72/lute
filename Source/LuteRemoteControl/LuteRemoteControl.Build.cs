using UnrealBuildTool;

public class LuteRemoteControl : ModuleRules
{
    public LuteRemoteControl(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "HTTPServer",
            "HTTP",
            "Json",
            "JsonUtilities",
            "ImageWrapper",
            "UnrealEd",
            "Landscape",
            "LandscapeEditor",
            "Foliage",
            "RenderCore",
            "RHI",
        });
    }
}
