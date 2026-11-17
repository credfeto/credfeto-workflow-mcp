using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("No board token is configured to authorise the GitHub request")]
public sealed partial class BoardTokenMissingException : Exception;
