using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The authentication status could not be read")]
public sealed partial class AuthStatusFailedException : Exception;
