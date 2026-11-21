using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The security advisories could not be listed")]
public sealed partial class SecurityAdvisoriesFailedException : Exception;
