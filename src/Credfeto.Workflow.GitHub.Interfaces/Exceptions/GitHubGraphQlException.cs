using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The GitHub GraphQL request failed")]
public sealed partial class GitHubGraphQlException : Exception;
