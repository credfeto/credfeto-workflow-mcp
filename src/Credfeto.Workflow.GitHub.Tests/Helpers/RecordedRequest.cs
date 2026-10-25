using System;
using System.Diagnostics;
using System.Net.Http;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

[DebuggerDisplay("Method={Method}, RequestUri={RequestUri}")]
internal sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Authorization, string? Body);
