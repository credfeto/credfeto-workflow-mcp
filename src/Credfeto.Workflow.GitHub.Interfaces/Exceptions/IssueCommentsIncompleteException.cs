using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The comments of the issue could not all be read, so none are returned")]
public sealed partial class IssueCommentsIncompleteException : Exception;
