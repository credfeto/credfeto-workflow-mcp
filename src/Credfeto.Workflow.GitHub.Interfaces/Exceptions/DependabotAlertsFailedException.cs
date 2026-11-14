using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The Dependabot alerts could not be listed")]
public sealed partial class DependabotAlertsFailedException : Exception;
