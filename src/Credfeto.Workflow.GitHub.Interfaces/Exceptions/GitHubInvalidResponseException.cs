using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The GitHub API returned a response that could not be read")]
public sealed partial class GitHubInvalidResponseException : Exception;
