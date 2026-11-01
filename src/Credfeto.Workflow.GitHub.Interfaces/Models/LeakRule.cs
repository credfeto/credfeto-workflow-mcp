namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// Declared in the order the rules are applied: the first rule that matches is the one reported
public enum LeakRule
{
    TOKEN,
    HOST_PATH,
    CONTROL_CHARACTER,
    INVALID_UTF8,
    TOO_LONG,
}
