using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("An issue-only change was refused because the number names a pull request")]
public sealed partial class PullRequestTargetRefusedException : Exception;
