using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repositories could not be searched")]
public sealed partial class RepoSearchFailedException : Exception;
