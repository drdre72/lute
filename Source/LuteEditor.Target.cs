using UnrealBuildTool;
using System.Collections.Generic;

public class LuteEditorTarget : TargetRules
{
    public LuteEditorTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.V5;
        ExtraModuleNames.Add("Lute");
    }
}
