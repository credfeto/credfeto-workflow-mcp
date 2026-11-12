using System;
using System.Collections.Generic;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Runs;

// Keeps only the last few lines, and makes only those safe to show
internal sealed class RunLogTailReader : RunLogLineReader
{
    private readonly int _count;
    private readonly Queue<(byte[] Bytes, bool Over)> _tail = new();

    public RunLogTailReader(int count)
    {
        this._count = count;
    }

    public override IReadOnlyList<RunLogEntry> Entries =>
        [
            .. this._tail.Select(line => new RunLogEntry(
                Line: RunLogSanitiser.Line(bytes: line.Bytes, over: line.Over),
                Number: null,
                Match: false
            )),
        ];

    public override int Matches => 0;

    public override int Kept => 0;

    protected override void Take(ReadOnlySpan<byte> bytes, bool over)
    {
        this._tail.Enqueue((Bytes: bytes.ToArray(), Over: over));

        if (this._tail.Count > this._count)
        {
            _ = this._tail.Dequeue();
        }
    }
}
