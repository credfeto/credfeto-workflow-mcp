using System;
using System.Collections.Generic;

namespace Credfeto.Workflow.GitHub.Runs;

// Splits a stream of bytes into lines, holding only the first bytes of each, so a long log or a long line never has
// to be held whole; what is kept of the lines is up to the reader
internal abstract class RunLogLineReader
{
    private const byte LINE_FEED = (byte)'\n';
    private const byte CARRIAGE_RETURN = (byte)'\r';

    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private readonly byte[] _line = new byte[RunLogSanitiser.MAX_LINE_BYTES + 1];
    private bool _firstLine;
    private int _kept;
    private long _length;

    public abstract IReadOnlyList<RunLogEntry> Entries { get; }

    // How many lines matched the search, and how many of those were kept; both are 0 when the log is not searched
    public abstract int Matches { get; }

    public abstract int Kept { get; }

    public void StartEntry()
    {
        this._firstLine = true;
        this._kept = 0;
        this._length = 0;
    }

    public void Add(ReadOnlySpan<byte> chunk)
    {
        ReadOnlySpan<byte> remaining = chunk;

        while (!remaining.IsEmpty)
        {
            int end = remaining.IndexOf(LINE_FEED);
            ReadOnlySpan<byte> part = end < 0 ? remaining : remaining[..end];
            this.Append(part);

            if (end < 0)
            {
                return;
            }

            this.Finish();
            remaining = remaining[(end + 1)..];
        }
    }

    // A last line with no line feed is still a line, but nothing after a final line feed is
    public void EndEntry()
    {
        if (this._length > 0)
        {
            this.Finish();
        }
    }

    // bytes holds at least the first RunLogSanitiser.MAX_LINE_BYTES + 1 bytes of a line that is over
    protected abstract void Take(ReadOnlySpan<byte> bytes, bool over);

    private void Append(ReadOnlySpan<byte> part)
    {
        int copy = Math.Min(val1: part.Length, val2: this._line.Length - this._kept);
        part[..copy].CopyTo(this._line.AsSpan(this._kept));
        this._kept += copy;
        this._length += part.Length;
    }

    private void Finish()
    {
        int start =
            this._firstLine && this._line.AsSpan(start: 0, length: this._kept).StartsWith(ByteOrderMark)
                ? ByteOrderMark.Length
                : 0;
        int end = this._kept;
        long length = this._length - start;

        // A carriage return ending a line of CRLF text is part of the line break, not the line; on a line too
        // long to keep whole the cut drops it anyway
        if (length == end - start && end > start && this._line[end - 1] == CARRIAGE_RETURN)
        {
            --end;
            --length;
        }

        this.Take(
            bytes: this._line.AsSpan(start: start, length: end - start),
            over: length > RunLogSanitiser.MAX_LINE_BYTES
        );

        this._firstLine = false;
        this._kept = 0;
        this._length = 0;
    }
}
