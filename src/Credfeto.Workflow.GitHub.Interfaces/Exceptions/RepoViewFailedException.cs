using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repository could not be viewed")]
public sealed partial class RepoViewFailedException : Exception;
