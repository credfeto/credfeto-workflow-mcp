using System;
using Credfeto.Workflow.Data.Models.Models;
using ModelContextProtocol;

namespace Credfeto.Workflow.Mcp;

// The SDK sends nothing unless the client asked for progress, so every read can be reported unconditionally
public sealed class PrChecksWaitProgress : IProgress<PrChecksResponse>
{
    private readonly IProgress<ProgressNotificationValue> _progress;
    private int _reads;

    public PrChecksWaitProgress(IProgress<ProgressNotificationValue> progress)
    {
        this._progress = progress;
    }

    public void Report(PrChecksResponse value)
    {
        this._reads++;

        this._progress.Report(
            new() { Progress = this._reads, Message = $"read {this._reads}: {value.Verdict} on {value.Sha}" }
        );
    }
}
