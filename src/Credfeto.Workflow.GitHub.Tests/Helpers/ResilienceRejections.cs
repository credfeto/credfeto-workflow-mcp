using System;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// Test cases name the rejection rather than pass its Type, which the AOT test generator cannot construct
internal static class ResilienceRejections
{
    public const string Timeout = nameof(TimeoutRejectedException);
    public const string RateLimiter = nameof(RateLimiterRejectedException);
    public const string BrokenCircuit = nameof(BrokenCircuitException);

    public static ExecutionRejectedException Create(string name)
    {
        return name switch
        {
            Timeout => new TimeoutRejectedException(),
            RateLimiter => new RateLimiterRejectedException(),
            BrokenCircuit => new BrokenCircuitException(),
            _ => throw new ArgumentOutOfRangeException(
                paramName: nameof(name),
                actualValue: name,
                message: "Unknown rejection"
            ),
        };
    }
}
