using System;
using System.Collections.Generic;
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
    // Entries are read as streams and only a tail or the matches of a search are kept, so this bounds the time a
    // compression bomb can take rather than the memory it can fill
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

    // False when the archive has no file for the step
    public ValueTask<bool> ReadStepAsync(
        string? jobName,
        int stepNumber,
        RunLogLineReader reader,
        CancellationToken cancellationToken
    )
    {
        string folder = FolderName(jobName) + "/";
        string prefix = stepNumber.ToString(CultureInfo.InvariantCulture) + "_";

        return this.ReadAsync(
            entries: this._entries.Where(entry => IsStepFile(entry: entry, folder: folder, prefix: prefix)),
            reader: reader,
            cancellationToken: cancellationToken
        );
    }

    // False when the archive has no whole-job file for the job
    public ValueTask<bool> ReadJobAsync(string? jobName, RunLogLineReader reader, CancellationToken cancellationToken)
    {
        string suffix = "_" + FolderName(jobName) + LOG_EXTENSION;

        return this.ReadAsync(
            entries: this._entries.Where(entry => IsJobFile(entry: entry, suffix: suffix)),
            reader: reader,
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

    private async ValueTask<bool> ReadAsync(
        IEnumerable<ZipArchiveEntry> entries,
        RunLogLineReader reader,
        CancellationToken cancellationToken
    )
    {
        ZipArchiveEntry[] matched = [.. entries];

        foreach (ZipArchiveEntry entry in matched)
        {
            await this.ReadEntryAsync(entry: entry, reader: reader, cancellationToken: cancellationToken);
        }

        return matched.Length != 0;
    }

    private async ValueTask ReadEntryAsync(
        ZipArchiveEntry entry,
        RunLogLineReader reader,
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
}
