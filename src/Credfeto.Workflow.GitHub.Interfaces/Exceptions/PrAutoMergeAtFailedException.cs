using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("When auto-merge was last enabled on the pull request could not be read")]
public sealed partial class PrAutoMergeAtFailedException : Exception;
