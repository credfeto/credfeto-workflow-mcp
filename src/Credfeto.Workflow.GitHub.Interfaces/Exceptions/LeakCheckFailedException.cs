using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The text could not be checked for leaks")]
public sealed partial class LeakCheckFailedException : Exception;
