using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull requests could not be listed")]
public sealed partial class PullRequestListFailedException : Exception;
