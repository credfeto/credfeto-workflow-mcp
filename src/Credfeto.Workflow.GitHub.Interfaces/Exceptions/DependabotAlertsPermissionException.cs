using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The caller's token is not allowed to read the repository's Dependabot alerts")]
public sealed partial class DependabotAlertsPermissionException : Exception;
