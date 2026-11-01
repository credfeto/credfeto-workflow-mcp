using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The requested Workflow status is not one of the board's states")]
public sealed partial class UnknownWorkflowStateException : Exception;
