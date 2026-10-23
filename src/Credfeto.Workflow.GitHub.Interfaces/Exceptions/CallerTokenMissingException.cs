using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("No caller bearer token is available to authorise the GitHub request")]
public sealed partial class CallerTokenMissingException : Exception;
