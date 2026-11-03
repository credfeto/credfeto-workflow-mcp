using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

// The issue exists by the time this is thrown, so its URL is carried both here and in the message and is never lost
[Description("The issue was created but could not be put on the Workflow board")]
public sealed partial class IssueCreateBoardWriteException : Exception
{
    public Uri? Url { get; init; }
}
