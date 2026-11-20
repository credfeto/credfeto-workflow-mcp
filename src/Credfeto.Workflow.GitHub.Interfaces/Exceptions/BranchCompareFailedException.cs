using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The comparison of two branches could not be read")]
public sealed partial class BranchCompareFailedException : Exception;
