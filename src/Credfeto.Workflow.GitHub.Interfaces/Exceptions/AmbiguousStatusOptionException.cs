using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("More than one project status option matches the requested name when case is ignored")]
public sealed partial class AmbiguousStatusOptionException : Exception;
