using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("A wait for the checks of a commit stopped because a read failed")]
public sealed partial class PrChecksWaitFailedException : Exception;
