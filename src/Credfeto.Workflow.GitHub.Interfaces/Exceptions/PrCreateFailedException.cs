using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull request could not be created")]
public sealed partial class PrCreateFailedException : Exception;
