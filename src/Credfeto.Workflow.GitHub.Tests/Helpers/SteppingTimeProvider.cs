using System;
using System.Collections.Generic;
using System.Threading;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// Time only moves when a delay is asked for or a test moves it, so a wait of minutes runs at once and the delays
// asked for can be checked
public sealed class SteppingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _delays = [];
    private long _now;

    public IReadOnlyList<TimeSpan> Delays => this._delays;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        return Interlocked.Read(ref this._now);
    }

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(location1: ref this._now, value: by.Ticks);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        this._delays.Add(dueTime);
        this.Advance(dueTime);
        callback(state);

        return new FiredTimer();
    }
}
