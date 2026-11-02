using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull request could not be read completely")]
public sealed partial class PullRequestViewFailedException : Exception;
