using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The security advisory could not be read")]
public sealed partial class AdvisoryViewFailedException : Exception;
