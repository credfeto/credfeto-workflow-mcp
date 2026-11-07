using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull request could not be closed or reopened")]
public sealed partial class PullRequestStateChangeFailedException : Exception;
