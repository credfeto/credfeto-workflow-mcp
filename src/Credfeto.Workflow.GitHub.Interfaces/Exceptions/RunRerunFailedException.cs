using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow run could not be rerun")]
public sealed partial class RunRerunFailedException : Exception;
