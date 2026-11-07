using System.Globalization;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Leaks;

namespace Credfeto.Workflow.GitHub.BodyRules;

// The checks a comment posted alongside a close or reopen must pass, shared so every state change refuses the same
// text, and named with cfwf's rule names so a refusal reads like "comment refused: host path (line 3)"
internal static class StateChangeCommentRules
{
    public const string APPROVAL_KEYWORD_RULE = "approval keyword";

    public static CommentRefusal? Check(ILeakChecker leakChecker, string comment)
    {
        if (leakChecker.CheckBody(comment) is { } leak)
        {
            return new(Rule: LeakRuleNames.Name(leak.Rule), LineNumber: leak.LineNumber);
        }

        return ApprovalKeywordMatcher.FindFirstKeywordLine(comment) is { } approval
            ? new(Rule: APPROVAL_KEYWORD_RULE, LineNumber: approval.LineNumber)
            : null;
    }

    public static string Message(CommentRefusal refusal)
    {
        return refusal.LineNumber is { } line
            ? $"comment refused: {refusal.Rule} (line {line.ToString(CultureInfo.InvariantCulture)})"
            : $"comment refused: {refusal.Rule}";
    }
}
