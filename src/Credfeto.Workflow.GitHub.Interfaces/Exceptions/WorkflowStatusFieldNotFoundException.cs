using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Workflow board has no Status or Workflow Status single-select field")]
public sealed partial class WorkflowStatusFieldNotFoundException : Exception;
