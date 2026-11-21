using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The labels of a pull request's closing issues could not be synced, and nothing was written")]
public sealed partial class PrSyncLabelsFailedException : Exception;
