using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issue could not be closed")]
public sealed partial class IssueCloseFailedException : Exception;
