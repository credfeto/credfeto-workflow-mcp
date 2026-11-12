using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Runs;

// Reads every line, numbering it from the start of the log, and keeps the matching lines with the lines around them.
// Each line is made safe to show before it is matched, so a pattern can never find text that would not be shown,
// such as a redacted token or a masked secret
internal sealed class RunLogSearchReader : RunLogLineReader
{
    private readonly Queue<RunLogEntry> _before = new();
    private readonly int _budget;
    private readonly List<RunLogEntry> _entries = [];
    private readonly RunLogSearch _search;
    private int _after;
    private int _kept;
    private int _matches;
    private int _number;

    // budget is how many more matches may be kept, which is less than the search's most when earlier steps kept some
    public RunLogSearchReader(RunLogSearch search, int budget)
    {
        this._search = search;
        this._budget = budget;
    }

    public override IReadOnlyList<RunLogEntry> Entries => this._entries;

    public override int Matches => this._matches;

    public override int Kept => this._kept;

    protected override void Take(ReadOnlySpan<byte> bytes, bool over)
    {
        ++this._number;

        if (this._number < this._search.FromLine)
        {
            return;
        }

        RunLogLine line = RunLogSanitiser.Line(bytes: bytes, over: over);
        bool match = this.IsMatch(line.Text);
        RunLogEntry entry = new(Line: line, Number: this._number, Match: match);

        if (match)
        {
            ++this._matches;

            if (this._kept < this._budget)
            {
                this.Keep(entry);

                return;
            }
        }

        if (this._after > 0)
        {
            --this._after;
            this._entries.Add(entry);
        }
        else if (this._kept < this._budget)
        {
            this.Remember(entry);
        }
    }

    private void Keep(RunLogEntry entry)
    {
        ++this._kept;
        this._entries.AddRange(this._before);
        this._before.Clear();
        this._entries.Add(entry);
        this._after = this._search.Context;
    }

    private void Remember(RunLogEntry entry)
    {
        if (this._search.Context == 0)
        {
            return;
        }

        this._before.Enqueue(entry);

        if (this._before.Count > this._search.Context)
        {
            _ = this._before.Dequeue();
        }
    }

    private bool IsMatch(string text)
    {
        try
        {
            return this._search.Pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new RunLogFailedException(
                message: "the grep pattern took too long to match a line of the log; use a simpler pattern",
                innerException: exception
            );
        }
    }
}
