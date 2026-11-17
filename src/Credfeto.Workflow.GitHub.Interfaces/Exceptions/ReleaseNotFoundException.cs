using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repository has no latest release")]
public sealed partial class ReleaseNotFoundException : Exception;
