using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The latest release of a repository could not be read")]
public sealed partial class ReleaseViewFailedException : Exception;
