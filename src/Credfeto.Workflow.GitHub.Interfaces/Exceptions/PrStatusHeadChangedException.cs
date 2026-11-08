using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The pull request head changed while its status was being read")]
public sealed partial class PrStatusHeadChangedException : Exception;
