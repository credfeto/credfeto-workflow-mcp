using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("not authenticated")]
public sealed partial class NotAuthenticatedException : Exception;
