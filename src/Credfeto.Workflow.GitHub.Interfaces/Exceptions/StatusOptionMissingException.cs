using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Workflow board's status field has no option for the requested state")]
public sealed partial class StatusOptionMissingException : Exception;
