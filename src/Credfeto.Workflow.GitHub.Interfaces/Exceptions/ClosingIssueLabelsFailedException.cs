using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The closing issues could not be read")]
public sealed partial class ClosingIssueLabelsFailedException : Exception;
