using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Http;

// RequestUri is the URI that answered, which differs from the one first requested once a redirect is followed
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Response.StatusCode} {RequestUri}")]
internal readonly record struct GitHubHttpExchange(HttpResponseMessage Response, Uri RequestUri);
