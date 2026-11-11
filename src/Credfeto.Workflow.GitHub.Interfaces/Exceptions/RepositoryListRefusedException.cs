using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Too many repositories to list")]
public sealed partial class RepositoryListRefusedException : Exception;
