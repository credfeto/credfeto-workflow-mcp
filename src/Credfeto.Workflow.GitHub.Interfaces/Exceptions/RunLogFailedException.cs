using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow run log could not be read")]
public sealed partial class RunLogFailedException : Exception;
