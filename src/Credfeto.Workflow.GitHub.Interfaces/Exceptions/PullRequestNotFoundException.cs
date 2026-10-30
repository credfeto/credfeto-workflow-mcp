using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull request does not exist in the repository")]
public sealed partial class PullRequestNotFoundException : Exception;
