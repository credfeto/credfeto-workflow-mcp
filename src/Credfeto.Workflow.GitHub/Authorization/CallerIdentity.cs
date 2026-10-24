using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.GitHub.Authorization;

// A class rather than a record so a generated ToString can never write the raw token to a log
[DebuggerDisplay("{CacheKey}")]
public sealed class CallerIdentity
{
    private CallerIdentity(string token)
    {
        this.Token = token;

        // A one-way hash so the raw token is never retained by the process-wide cache
        this.CacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public string Token { get; }

    public string CacheKey { get; }

    // Resolved once per request so everything acting for the caller sees the same identity, whatever later
    // happens to the request headers
    public static bool TryResolve(HttpContext context)
    {
        string? token = CallerBearerToken.FromRequest(context.Request);

        if (token is null)
        {
            return false;
        }

        context.Features.Set(new CallerIdentity(token));

        return true;
    }

    public static CallerIdentity? Of(HttpContext? context)
    {
        return context?.Features.Get<CallerIdentity>();
    }
}
