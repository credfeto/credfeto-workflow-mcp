using System;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Credfeto.Workflow.GitHub.Authorization;

public static class CallerBearerToken
{
    public const string Scheme = "Bearer";

    public static string? FromRequest(HttpRequest request)
    {
        StringValues values = request.Headers.Authorization;

        if (values.Count != 1)
        {
            return null;
        }

        if (!AuthenticationHeaderValue.TryParse(input: values[0], out AuthenticationHeaderValue? header))
        {
            return null;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(x: header.Scheme, y: Scheme))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(header.Parameter) ? null : header.Parameter;
    }
}
