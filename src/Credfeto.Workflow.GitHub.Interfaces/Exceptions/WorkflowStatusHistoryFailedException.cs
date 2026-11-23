using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Workflow status history of an issue or pull request could not be read")]
public sealed partial class WorkflowStatusHistoryFailedException : Exception;
