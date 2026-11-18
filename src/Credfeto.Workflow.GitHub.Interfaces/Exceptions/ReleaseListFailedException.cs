using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The releases could not be listed")]
public sealed partial class ReleaseListFailedException : Exception;
