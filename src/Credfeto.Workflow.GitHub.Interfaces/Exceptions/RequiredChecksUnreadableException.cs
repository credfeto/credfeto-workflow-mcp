using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The required checks of a branch could not be read")]
public sealed partial class RequiredChecksUnreadableException : Exception;
