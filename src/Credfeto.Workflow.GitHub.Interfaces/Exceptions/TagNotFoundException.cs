using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The tag was not found in the repository")]
public sealed partial class TagNotFoundException : Exception;
