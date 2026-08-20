using UnrealBuildTool;
using System.Collections.Generic;

public class LuteTarget : TargetRules
{
    public LuteTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Game;
        DefaultBuildSettings = BuildSettingsVersion.V5;
        ExtraModuleNames.Add("Lute");
    }
}
