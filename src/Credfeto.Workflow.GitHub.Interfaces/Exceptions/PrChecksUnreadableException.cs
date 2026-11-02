using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The checks of a commit could not be read")]
public sealed partial class PrChecksUnreadableException : Exception;
