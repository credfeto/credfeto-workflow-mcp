using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Workflow project linked to the repository could not be read or was not in the expected form")]
public sealed partial class WorkflowBoardInvalidException : Exception;
