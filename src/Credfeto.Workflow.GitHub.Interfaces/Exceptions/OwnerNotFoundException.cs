using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The owner was not found")]
public sealed partial class OwnerNotFoundException : Exception;
