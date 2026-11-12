using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The labels could not be listed")]
public sealed partial class LabelListFailedException : Exception;
