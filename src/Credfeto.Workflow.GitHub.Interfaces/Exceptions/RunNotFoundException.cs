using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow run was not found")]
public sealed partial class RunNotFoundException : Exception;
