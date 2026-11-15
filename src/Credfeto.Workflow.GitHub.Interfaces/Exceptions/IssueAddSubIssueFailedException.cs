using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The sub-issue could not be added")]
public sealed partial class IssueAddSubIssueFailedException : Exception;
