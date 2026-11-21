using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The security advisory that was asked for was not found")]
public sealed partial class AdvisoryNotFoundException : Exception;
