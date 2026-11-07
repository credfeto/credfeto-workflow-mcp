using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull requests could not be searched")]
public sealed partial class PrSearchFailedException : Exception;
