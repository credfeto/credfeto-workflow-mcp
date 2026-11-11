using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repositories could not be listed")]
public sealed partial class RepositoryListFailedException : Exception;
