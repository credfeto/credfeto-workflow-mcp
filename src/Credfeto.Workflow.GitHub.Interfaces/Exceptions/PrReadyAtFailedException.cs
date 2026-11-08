using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("When the pull request became ready for review could not be read")]
public sealed partial class PrReadyAtFailedException : Exception;
