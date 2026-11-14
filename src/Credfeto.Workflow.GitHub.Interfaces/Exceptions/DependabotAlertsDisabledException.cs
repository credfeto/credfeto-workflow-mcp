using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Dependabot alerts are disabled for the repository")]
public sealed partial class DependabotAlertsDisabledException : Exception;
