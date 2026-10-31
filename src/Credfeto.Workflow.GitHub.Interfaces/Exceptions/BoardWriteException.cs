using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("A work item could not be added to the Workflow board or its status could not be set")]
public sealed partial class BoardWriteException : Exception;
