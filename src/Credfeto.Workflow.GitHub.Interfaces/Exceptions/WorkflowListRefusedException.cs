using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Too many workflows to list")]
public sealed partial class WorkflowListRefusedException : Exception;
