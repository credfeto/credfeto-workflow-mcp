using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issue could not be read")]
public sealed partial class IssueViewFailedException : Exception;
