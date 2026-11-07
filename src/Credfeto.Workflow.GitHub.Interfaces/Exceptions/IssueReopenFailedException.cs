using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issue could not be reopened")]
public sealed partial class IssueReopenFailedException : Exception;
