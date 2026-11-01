using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Workflow board's status fields could not be read")]
public sealed partial class StatusFieldReadFailedException : Exception;
