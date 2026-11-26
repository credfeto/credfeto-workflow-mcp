using System;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// A timer that has already fired once and never fires again
public sealed class FiredTimer : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return false;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
