using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The commit a tag points to could not be read")]
public sealed partial class TagViewFailedException : Exception;
