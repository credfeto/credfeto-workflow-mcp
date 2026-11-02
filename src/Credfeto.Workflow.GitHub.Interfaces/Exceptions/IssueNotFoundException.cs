using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The issue does not exist in the repository")]
public sealed partial class IssueNotFoundException : Exception;
