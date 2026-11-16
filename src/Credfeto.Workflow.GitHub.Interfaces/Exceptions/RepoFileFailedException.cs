using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repository file could not be read")]
public sealed partial class RepoFileFailedException : Exception;
