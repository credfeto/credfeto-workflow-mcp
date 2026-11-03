using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The labels could not be changed")]
public sealed partial class LabelChangeFailedException : Exception;
