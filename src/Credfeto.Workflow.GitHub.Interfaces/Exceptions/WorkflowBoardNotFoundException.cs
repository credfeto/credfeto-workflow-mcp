using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("No open project titled Workflow is linked to the repository")]
public sealed partial class WorkflowBoardNotFoundException : Exception;
