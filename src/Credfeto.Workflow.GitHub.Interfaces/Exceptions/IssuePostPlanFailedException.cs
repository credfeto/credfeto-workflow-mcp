using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

// The steps already done are carried both here and in the message, so a part-way failure never loses the comment URL
[Description("Posting the plan failed part-way")]
public sealed partial class IssuePostPlanFailedException : Exception
{
    public Uri? CommentUrl { get; init; }

    public IReadOnlyList<string> StepsDone { get; init; } = [];
}
