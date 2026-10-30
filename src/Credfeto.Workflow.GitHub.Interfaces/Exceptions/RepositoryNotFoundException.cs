using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repository was not found, or the caller cannot see it")]
public sealed partial class RepositoryNotFoundException : Exception;
