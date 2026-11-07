using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow was not found in the repository")]
public sealed partial class WorkflowNotFoundException : Exception;
