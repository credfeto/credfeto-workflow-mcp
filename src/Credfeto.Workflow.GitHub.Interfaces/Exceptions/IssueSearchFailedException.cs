using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issues could not be searched")]
public sealed partial class IssueSearchFailedException : Exception;
