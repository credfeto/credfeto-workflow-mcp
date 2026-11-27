using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

// The issue exists by the time this is thrown, so its URL is carried both here and in the message and is never lost
[Description("The issue was created but an assignee asked for was not assigned")]
public sealed partial class IssueCreateNotAssignedException : Exception
{
    public Uri? Url { get; init; }
}
