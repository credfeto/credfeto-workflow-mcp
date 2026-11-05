using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The open issues could not be listed")]
public sealed partial class IssueListFailedException : Exception;
