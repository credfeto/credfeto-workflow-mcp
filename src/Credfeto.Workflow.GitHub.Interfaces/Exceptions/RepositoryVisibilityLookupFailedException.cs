using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The visibility of a repository referenced in text being written could not be read")]
public sealed partial class RepositoryVisibilityLookupFailedException : Exception;
