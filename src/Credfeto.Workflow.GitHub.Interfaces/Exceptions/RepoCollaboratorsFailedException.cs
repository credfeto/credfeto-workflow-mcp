using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The collaborators could not be listed")]
public sealed partial class RepoCollaboratorsFailedException : Exception;
