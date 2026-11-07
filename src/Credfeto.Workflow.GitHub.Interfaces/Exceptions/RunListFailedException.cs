using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The workflow runs could not be listed")]
public sealed partial class RunListFailedException : Exception;
