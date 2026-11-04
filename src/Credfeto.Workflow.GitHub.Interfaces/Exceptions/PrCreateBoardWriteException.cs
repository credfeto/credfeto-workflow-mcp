using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

// The pull request exists by the time this is thrown, so its URL is carried both here and in the message and is never
// lost
[Description("The pull request exists but could not be put on the Workflow board")]
public sealed partial class PrCreateBoardWriteException : Exception
{
    public Uri? Url { get; init; }
}
