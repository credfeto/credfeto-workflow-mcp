using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Runs;

// GitHub's log archive holds a folder for each job, named after the job, with a file for each step starting with
// the step's number, and beside the folders a file for each job's whole log
internal sealed class RunLogArchive : IDisposable
{
    // Entries are read as streams and only a tail of lines is kept, so this bounds the time a compression bomb can
    // take rather than the memory it can fill
    private const long MAX_DECOMPRESSED_BYTES = 256L * 1024 * 1024;

    // GitHub drops these from a job's name, then keeps at most this many characters of it, to name its folder
    private const int MAX_FOLDER_NAME_LENGTH = 90;

    private const int BUFFER_SIZE = 81920;

    private const string LOG_EXTENSION = ".txt";

    private readonly ZipArchive _archive;
    private readonly IReadOnlyList<ZipArchiveEntry> _entries;
    private long _remaining;

    public RunLogArchive(byte[] content)
    {
        this._archive = new(
            stream: new MemoryStream(buffer: content, writable: false),
            mode: ZipArchiveMode.Read,
            leaveOpen: false
        );
        this._entries =
        [
            .. this._archive.Entries.OrderBy(keySelector: entry => entry.FullName, comparer: StringComparer.Ordinal),
        ];
        this._remaining = MAX_DECOMPRESSED_BYTES;
    }

    public void Dispose()
    {
        this._archive.Dispose();
    }

    // Null when the archive has no file for the step
    public ValueTask<IReadOnlyList<RawLine>?> StepLinesAsync(
        string? jobName,
        int stepNumber,
        int lines,
        CancellationToken cancellationToken
    )
    {
        string folder = FolderName(jobName) + "/";
        string prefix = stepNumber.ToString(CultureInfo.InvariantCulture) + "_";

        return this.ReadTailAsync(
            entries: this._entries.Where(entry => IsStepFile(entry: entry, folder: folder, prefix: prefix)),
            lines: lines,
            cancellationToken: cancellationToken
        );
    }

    // Null when the archive has no whole-job file for the job
    public ValueTask<IReadOnlyList<RawLine>?> JobLinesAsync(
        string? jobName,
        int lines,
        CancellationToken cancellationToken
    )
    {
        string suffix = "_" + FolderName(jobName) + LOG_EXTENSION;

        return this.ReadTailAsync(
            entries: this._entries.Where(entry => IsJobFile(entry: entry, suffix: suffix)),
            lines: lines,
            cancellationToken: cancellationToken
        );
    }

    private static string FolderName(string? jobName)
    {
        string name = (jobName ?? string.Empty)
            .Replace(oldValue: "/", newValue: "", comparisonType: StringComparison.Ordinal)
            .Replace(oldValue: ":", newValue: "", comparisonType: StringComparison.Ordinal);

        return name[..Math.Min(val1: MAX_FOLDER_NAME_LENGTH, val2: name.Length)].Trim();
    }

    private static bool IsStepFile(ZipArchiveEntry entry, string folder, string prefix)
    {
        string path = entry.FullName;

        if (!path.StartsWith(value: folder, comparisonType: StringComparison.Ordinal))
        {
            return false;
        }

        string file = path[folder.Length..];

        return !file.Contains('/', StringComparison.Ordinal)
            && file.StartsWith(value: prefix, comparisonType: StringComparison.Ordinal)
            && file.EndsWith(value: LOG_EXTENSION, comparisonType: StringComparison.Ordinal);
    }

    // A whole-job file is named <number>_<job>.txt, and its number can be negative
    private static bool IsJobFile(ZipArchiveEntry entry, string suffix)
    {
        string path = entry.FullName;

        if (
            path.Contains('/', StringComparison.Ordinal)
            || !path.EndsWith(value: suffix, comparisonType: StringComparison.Ordinal)
        )
        {
            return false;
        }

        ReadOnlySpan<char> number = path.AsSpan(start: 0, length: path.Length - suffix.Length);

        if (number.StartsWith('-'))
        {
            number = number[1..];
        }

        return !number.IsEmpty && !number.ContainsAnyExceptInRange(lowInclusive: '0', highInclusive: '9');
    }

    private async ValueTask<IReadOnlyList<RawLine>?> ReadTailAsync(
        IEnumerable<ZipArchiveEntry> entries,
        int lines,
        CancellationToken cancellationToken
    )
    {
        ZipArchiveEntry[] matched = [.. entries];

        if (matched.Length == 0)
        {
            return null;
        }

        TailReader reader = new(lines);

        foreach (ZipArchiveEntry entry in matched)
        {
            await this.ReadEntryAsync(entry: entry, reader: reader, cancellationToken: cancellationToken);
        }

        return reader.Lines;
    }

    private async ValueTask ReadEntryAsync(
        ZipArchiveEntry entry,
        TailReader reader,
        CancellationToken cancellationToken
    )
    {
        await using Stream stream = await entry.OpenAsync(cancellationToken);
        byte[] buffer = new byte[BUFFER_SIZE];

        reader.StartEntry();

        for (
            int read = await stream.ReadAsync(buffer: buffer, cancellationToken: cancellationToken);
            read > 0;
            read = await stream.ReadAsync(buffer: buffer, cancellationToken: cancellationToken)
        )
        {
            this.Consume(read);
            reader.Add(buffer.AsSpan(start: 0, length: read));
        }

        reader.EndEntry();
    }

    private void Consume(int read)
    {
        this._remaining -= read;

        if (this._remaining < 0)
        {
            throw new RunLogFailedException("the run's logs are too large to read");
        }
    }

    [DebuggerDisplay("{Bytes.Length} bytes, over: {Over}")]
    public readonly record struct RawLine(byte[] Bytes, bool Over);

    // Splits a stream of bytes into lines, keeping only the last few lines and the first bytes of each, so a long
    // log or a long line never has to be held whole
    private sealed class TailReader
    {
        private const byte LINE_FEED = (byte)'\n';
        private const byte CARRIAGE_RETURN = (byte)'\r';

        private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

        private readonly int _count;
        private readonly byte[] _line = new byte[RunLogSanitiser.MAX_LINE_BYTES + 1];
        private readonly Queue<RawLine> _tail = new();
        private bool _firstLine;
        private int _kept;
        private long _length;

        public TailReader(int count)
        {
            this._count = count;
        }

        public IReadOnlyList<RawLine> Lines => [.. this._tail];

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

            this._tail.Enqueue(new(Bytes: this._line[start..end], Over: length > RunLogSanitiser.MAX_LINE_BYTES));

            if (this._tail.Count > this._count)
            {
                _ = this._tail.Dequeue();
            }

            this._firstLine = false;
            this._kept = 0;
            this._length = 0;
        }
    }
}
