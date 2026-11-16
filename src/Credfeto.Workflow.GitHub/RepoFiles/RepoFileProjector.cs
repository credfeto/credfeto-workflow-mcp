using System;
using System.Linq;
using System.Text;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.RepoFiles;

internal static class RepoFileProjector
{
    public const int MAX_FILE_BYTES = 1048576;
    public const int MAX_DIRECTORY_ENTRIES = 1000;

    private const string BASE64_ENCODING = "base64";

    // A strict decoder throws on invalid bytes rather than substituting U+FFFD, so binary content is never passed off
    // as text; GetString keeps a byte order mark, so the content stays byte for byte what the file holds
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public static RepoFileResponse Project(RepoContentResponse response, RepoFileRequest request)
    {
        return response.Type switch
        {
            "file" => File(response: response, request: request),
            "dir" => Directory(response: response, request: request),
            "symlink" => Refused(request: request, message: "the path is a symlink that GitHub does not follow"),
            "submodule" => Refused(request: request, message: "the path is a submodule"),
            _ => throw new GitHubInvalidResponseException("GitHub returned content of an unknown type"),
        };
    }

    private static RepoFileResponse File(RepoContentResponse response, RepoFileRequest request)
    {
        if (response.Size is > MAX_FILE_BYTES)
        {
            return TooLarge(request);
        }

        // GitHub sends no content, with an encoding of none, for a file it will not inline, so that is refused rather
        // than read as an empty file
        if (response.Content is null || !StringComparer.Ordinal.Equals(x: response.Encoding, y: BASE64_ENCODING))
        {
            return Refused(request: request, message: "GitHub did not send the content of the file");
        }

        byte[] bytes = DecodeBase64(response.Content);

        if (bytes.Length > MAX_FILE_BYTES)
        {
            return TooLarge(request);
        }

        if (bytes.AsSpan().Contains((byte)0))
        {
            return Refused(request: request, message: "the file contains a NUL byte, so it is treated as binary");
        }

        return DecodeText(bytes) is { } text
            ? new(
                Kind: RepoFileKind.File,
                Message: "the file was read",
                Repository: request.Repository,
                Path: request.Path,
                Ref: request.GitRef,
                Size: bytes.Length,
                Content: text,
                Entries: null
            )
            : Refused(request: request, message: "the file is not valid UTF-8 text");
    }

    private static RepoFileResponse Directory(RepoContentResponse response, RepoFileRequest request)
    {
        RepoContentEntryResponse?[] entries = response.Entries
            ?? throw new GitHubInvalidResponseException("GitHub returned a directory without entries");

        // GitHub lists at most 1000 entries, so a listing that long may have been cut short
        if (entries.Length >= MAX_DIRECTORY_ENTRIES)
        {
            return Refused(
                request: request,
                message: "the directory has 1000 entries or more, so GitHub may not have listed all of them"
            );
        }

        return new(
            Kind: RepoFileKind.Directory,
            Message: "the directory was listed",
            Repository: request.Repository,
            Path: request.Path,
            Ref: request.GitRef,
            Size: null,
            Content: null,
            Entries:
            [
                .. entries.Select(Entry).OrderBy(keySelector: entry => entry.Name, comparer: StringComparer.Ordinal),
            ]
        );
    }

    private static RepoFileEntry Entry(RepoContentEntryResponse? entry)
    {
        return entry is { Type: { } type, Name: { } name }
            ? new(Type: WithoutControlCharacters(type), Name: WithoutControlCharacters(name))
            : throw new GitHubInvalidResponseException("GitHub returned a directory entry without a type or name");
    }

    private static string WithoutControlCharacters(string value)
    {
        return string.Create(length: value.Length, state: value, action: static (span, source) =>
        {
            for (int index = 0; index < source.Length; ++index)
            {
                span[index] = char.IsControl(source[index]) ? ' ' : source[index];
            }
        });
    }

    private static byte[] DecodeBase64(string content)
    {
        try
        {
            // GitHub wraps the base64 text in lines, and the decoder skips that whitespace
            return Convert.FromBase64String(content);
        }
        catch (FormatException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned file content that is not valid base64",
                innerException: exception
            );
        }
    }

    private static string? DecodeText(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static RepoFileResponse TooLarge(RepoFileRequest request)
    {
        return Refused(request: request, message: "the file is larger than 1 MiB");
    }

    private static RepoFileResponse Refused(RepoFileRequest request, string message)
    {
        return new(
            Kind: RepoFileKind.Refused,
            Message: message,
            Repository: request.Repository,
            Path: request.Path,
            Ref: request.GitRef,
            Size: null,
            Content: null,
            Entries: null
        );
    }
}
