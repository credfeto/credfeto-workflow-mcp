using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Leaks;

// The rule names cfwf prints in a refusal, such as "body refused: host path (line 3)"
internal static class LeakRuleNames
{
    public static string Name(LeakRule rule)
    {
        return rule switch
        {
            LeakRule.TOKEN => "token",
            LeakRule.HOST_PATH => "host path",
            LeakRule.CONTROL_CHARACTER => "control character",
            LeakRule.INVALID_UTF8 => "invalid UTF-8",
            LeakRule.TOO_LONG => "too long",
            _ => "private repository",
        };
    }
}
