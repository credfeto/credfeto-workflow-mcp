using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The code could not be searched")]
public sealed partial class CodeSearchFailedException : Exception;
