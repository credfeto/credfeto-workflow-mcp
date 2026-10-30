using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The comments of the pull request could not all be read, so none are returned")]
public sealed partial class PullRequestCommentsIncompleteException : Exception;
