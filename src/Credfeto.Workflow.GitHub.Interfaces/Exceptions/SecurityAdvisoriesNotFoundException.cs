using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The repository whose security advisories were asked for was not found")]
public sealed partial class SecurityAdvisoriesNotFoundException : Exception;
