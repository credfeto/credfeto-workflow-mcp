using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description(
    "The Workflow board status of a work item could not be read by either the direct query or the board listing"
)]
public sealed partial class BoardReadFailedException : Exception;
