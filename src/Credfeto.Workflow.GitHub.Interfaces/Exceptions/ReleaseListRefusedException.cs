using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Too many releases to list")]
public sealed partial class ReleaseListRefusedException : Exception;
