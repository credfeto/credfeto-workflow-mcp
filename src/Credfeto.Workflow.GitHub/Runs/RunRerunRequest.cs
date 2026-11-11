using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("Debug logging: {EnableDebugLogging}")]
internal sealed record RunRerunRequest([property: JsonPropertyName("enable_debug_logging")] bool EnableDebugLogging);
