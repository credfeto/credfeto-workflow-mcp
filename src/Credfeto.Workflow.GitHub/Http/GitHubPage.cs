using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Http;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("NextUri={NextUri}")]
internal readonly record struct GitHubPage<TContent>(TContent Content, string? NextUri);
