using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow could not be run")]
public sealed partial class WorkflowRunFailedException : Exception;
