using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issue, or a label it needs, could not be created")]
public sealed partial class IssueCreateFailedException : Exception;
