using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.RepoFiles;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.RepoFiles;

public sealed class RepoFileServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string PATH = "README.md";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string OBJECT_MEDIA_TYPE = "application/vnd.github.object+json";
    private const string FAILED = "could not read the content of credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the path or ref was not found in the repository credfeto/credfeto-workflow-mcp, or the repository was not found, or the caller cannot see it";
    private const int MAX_FILE_BYTES = 1048576;

    private static string FileJson(byte[] bytes)
    {
        return FileJsonOf(
            size: bytes.Length.ToString(CultureInfo.InvariantCulture),
            encoding: "\"base64\"",
            content: "\"" + Convert.ToBase64String(bytes) + "\""
        );
    }

    private static string FileJsonOf(string size, string encoding, string content)
    {
        return "{\"type\":\"file\",\"size\":" + size + ",\"encoding\":" + encoding + ",\"content\":" + content + "}";
    }

    private static string DirectoryJson(params string[] entries)
    {
        return "{\"type\":\"dir\",\"entries\":[" + string.Join(separator: ',', value: entries) + "]}";
    }

    private static string EntryJson(string type, string name)
    {
        return "{\"type\":\"" + type + "\",\"name\":\"" + name + "\"}";
    }

    private static string[] Entries(int count)
    {
        return
        [
            .. Enumerable
                .Range(start: 0, count: count)
                .Select(index => EntryJson(type: "file", name: "f" + index.ToString(CultureInfo.InvariantCulture))),
        ];
    }

    private static byte[] Bytes(int count)
    {
        return [.. Enumerable.Repeat(element: (byte)'a', count: count)];
    }

    private static Uri ContentUri(string suffix)
    {
        return new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/contents/" + suffix);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private RepoFileService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<RepoFileResponse> ReadAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string path = PATH,
        string? gitRef = null
    )
    {
        return await this.CreateService(upstream)
            .ReadAsync(repository: repository, path: path, gitRef: gitRef, cancellationToken: this.CancellationToken());
    }

    private async Task<RepoFileResponse> ReadJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ReadAsync(upstream);
    }

    private async Task<RepoFileFailedException> ReadFailsAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        RepoFileFailedException exception = await Assert.ThrowsAsync<RepoFileFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    private static void AssertRefused(RepoFileResponse response, string message)
    {
        Assert.Equal(expected: RepoFileKind.Refused, actual: response.Kind);
        Assert.Equal(expected: message, actual: response.Message);
        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: PATH, actual: response.Path);
        Assert.Null(response.Content);
        Assert.Null(response.Entries);
        Assert.Null(response.Size);
    }

    [Fact]
    public async Task ReadsATextFileAtTheDefaultBranchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(FileJson(Encoding.UTF8.GetBytes("hello")))
        );

        RepoFileResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: RepoFileKind.File, actual: response.Kind);
        Assert.Equal(expected: "the file was read", actual: response.Message);
        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: PATH, actual: response.Path);
        Assert.Null(response.Ref);
        Assert.Equal(expected: 5, actual: response.Size);
        Assert.Equal(expected: "hello", actual: response.Content);
        Assert.Null(response.Entries);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ContentUri(PATH), actual: request.RequestUri);
        Assert.Equal(expected: OBJECT_MEDIA_TYPE, actual: request.Accept);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData(SHA)]
    public async Task PassesTheRefThroughAsync(string gitRef)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(FileJson(Encoding.UTF8.GetBytes("x"))));

        RepoFileResponse response = await this.ReadAsync(upstream: upstream, gitRef: gitRef);

        Assert.Equal(expected: RepoFileKind.File, actual: response.Kind);
        Assert.Equal(expected: gitRef, actual: response.Ref);
        Assert.Equal(
            expected: ContentUri(PATH + "?ref=" + gitRef).AbsoluteUri,
            actual: Assert.Single(upstream.Requests).RequestUri?.AbsoluteUri
        );
    }

    [Fact]
    public async Task EscapesTheRefAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(FileJson(Encoding.UTF8.GetBytes("x"))));

        _ = await this.ReadAsync(upstream: upstream, gitRef: "feature/a&b");

        Assert.Equal(
            expected: ContentUri(PATH + "?ref=feature%2Fa%26b").AbsoluteUri,
            actual: Assert.Single(upstream.Requests).RequestUri?.AbsoluteUri
        );
    }

    [Fact]
    public async Task EscapesEachPartOfThePathAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(FileJson(Encoding.UTF8.GetBytes("x"))));

        _ = await this.ReadAsync(upstream: upstream, path: "docs/a b#c?.md");

        Assert.Equal(
            expected: ContentUri("docs/a%20b%23c%3F.md").AbsoluteUri,
            actual: Assert.Single(upstream.Requests).RequestUri?.AbsoluteUri
        );
    }

    [Fact]
    public async Task KeepsTheContentByteForByteAsync()
    {
        const string text = "﻿line one\r\nline two\né\U0001F600";

        // GetBytes encodes the leading U+FEFF as the three byte order mark bytes, which must survive the read
        RepoFileResponse response = await this.ReadJsonAsync(FileJson(Encoding.UTF8.GetBytes(text)));

        Assert.Equal(expected: RepoFileKind.File, actual: response.Kind);
        Assert.Equal(expected: text, actual: response.Content);
        Assert.Equal(expected: Encoding.UTF8.GetByteCount(text), actual: response.Size);
    }

    [Fact]
    public async Task DecodesBase64WrappedInLinesAsync()
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello world"));
        string wrapped = encoded[..8] + "\\n" + encoded[8..] + "\\n";

        RepoFileResponse response = await this.ReadJsonAsync(
            FileJsonOf(size: "11", encoding: "\"base64\"", content: "\"" + wrapped + "\"")
        );

        Assert.Equal(expected: "hello world", actual: response.Content);
    }

    [Fact]
    public async Task ReadsAnEmptyFileAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(FileJson([]));

        Assert.Equal(expected: RepoFileKind.File, actual: response.Kind);
        Assert.Equal(expected: string.Empty, actual: response.Content);
        Assert.Equal(expected: 0, actual: response.Size);
    }

    [Fact]
    public async Task ReadsAFileOfExactlyTheMaximumSizeAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(FileJson(Bytes(MAX_FILE_BYTES)));

        Assert.Equal(expected: RepoFileKind.File, actual: response.Kind);
        Assert.Equal(expected: MAX_FILE_BYTES, actual: response.Size);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task MissingPathOrRefIsNotFoundWithoutNamingThemAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithJson(
                statusCode: status,
                json: "{\"message\":\"No commit found for the ref secret-ref\"}"
            )
        );

        RepoFileResponse response = await this.ReadAsync(
            upstream: upstream,
            path: "secret/path.txt",
            gitRef: "secret-ref"
        );

        Assert.Equal(expected: RepoFileKind.NotFound, actual: response.Kind);
        Assert.Equal(expected: NOT_FOUND, actual: response.Message);
        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Null(response.Path);
        Assert.Null(response.Ref);
        Assert.Null(response.Size);
        Assert.Null(response.Content);
        Assert.Null(response.Entries);
        Assert.DoesNotContain(expectedSubstring: "secret", actualString: response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsADirectorySortedOrdinallyAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(
            DirectoryJson(
                EntryJson(type: "file", name: "b.md"),
                EntryJson(type: "dir", name: "a"),
                EntryJson(type: "file", name: "B.md"),
                EntryJson(type: "submodule", name: "_lib")
            )
        );

        Assert.Equal(expected: RepoFileKind.Directory, actual: response.Kind);
        Assert.Equal(expected: "the directory was listed", actual: response.Message);
        Assert.Equal(expected: PATH, actual: response.Path);
        Assert.Null(response.Content);
        Assert.Null(response.Size);
        IReadOnlyList<RepoFileEntry> entries = Assert.IsAssignableFrom<IReadOnlyList<RepoFileEntry>>(response.Entries);
        Assert.Equal(
            expected:
            [
                new RepoFileEntry(Type: "file", Name: "B.md"),
                new RepoFileEntry(Type: "submodule", Name: "_lib"),
                new RepoFileEntry(Type: "dir", Name: "a"),
                new RepoFileEntry(Type: "file", Name: "b.md"),
            ],
            actual: entries
        );
    }

    [Fact]
    public async Task ReplacesControlCharactersInEntryNamesAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(
            DirectoryJson(EntryJson(type: "file", name: "a\\nb\\u001bc\\u0085d"))
        );

        RepoFileEntry entry = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<RepoFileEntry>>(response.Entries));
        Assert.Equal(expected: "a b c d", actual: entry.Name);
    }

    [Fact]
    public async Task ListsAnEmptyDirectoryAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(DirectoryJson());

        Assert.Equal(expected: RepoFileKind.Directory, actual: response.Kind);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<RepoFileEntry>>(response.Entries));
    }

    [Fact]
    public async Task ListsADirectoryOfNineHundredAndNinetyNineEntriesAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(DirectoryJson(Entries(999)));

        Assert.Equal(expected: RepoFileKind.Directory, actual: response.Kind);
        Assert.Equal(expected: 999, actual: response.Entries?.Count);
    }

    [Fact]
    public async Task RefusesADirectoryOfAThousandEntriesAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(DirectoryJson(Entries(1000)));

        AssertRefused(
            response: response,
            message: "the directory has 1000 entries or more, so GitHub may not have listed all of them"
        );
    }

    [Fact]
    public async Task RefusesAFileWithANulByteAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(FileJson([(byte)'a', 0, (byte)'b']));

        AssertRefused(response: response, message: "the file contains a NUL byte, so it is treated as binary");
    }

    [Theory]
    [InlineData("FFFE41")]
    [InlineData("61C3")]
    [InlineData("EDA080")]
    public async Task RefusesAFileThatIsNotUtf8Async(string hex)
    {
        RepoFileResponse response = await this.ReadJsonAsync(FileJson(Convert.FromHexString(hex)));

        AssertRefused(response: response, message: "the file is not valid UTF-8 text");
    }

    [Fact]
    public async Task RefusesAFileGitHubReportsAsTooLargeAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync(
            FileJsonOf(size: "1048577", encoding: "\"none\"", content: "\"\"")
        );

        AssertRefused(response: response, message: "the file is larger than 1 MiB");
    }

    [Fact]
    public async Task RefusesAFileWhoseContentIsLargerThanTheCapAsync()
    {
        string content = Convert.ToBase64String(Bytes(MAX_FILE_BYTES + 1));

        RepoFileResponse response = await this.ReadJsonAsync(
            FileJsonOf(size: "1", encoding: "\"base64\"", content: "\"" + content + "\"")
        );

        AssertRefused(response: response, message: "the file is larger than 1 MiB");
    }

    [Theory]
    [InlineData("\"none\"", "\"\"")]
    [InlineData("\"base64\"", "null")]
    [InlineData("null", "\"aGVsbG8=\"")]
    [InlineData("\"utf-8\"", "\"hello\"")]
    public async Task RefusesAFileWhoseContentWasNotSentAsync(string encoding, string content)
    {
        RepoFileResponse response = await this.ReadJsonAsync(
            FileJsonOf(size: "5", encoding: encoding, content: content)
        );

        AssertRefused(response: response, message: "GitHub did not send the content of the file");
    }

    [Fact]
    public async Task RefusesASymlinkGitHubDoesNotFollowAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync("{\"type\":\"symlink\",\"target\":\"../../etc/passwd\"}");

        AssertRefused(response: response, message: "the path is a symlink that GitHub does not follow");
    }

    [Fact]
    public async Task RefusesASubmoduleAsync()
    {
        RepoFileResponse response = await this.ReadJsonAsync("{\"type\":\"submodule\",\"submodule_git_url\":\"x\"}");

        AssertRefused(response: response, message: "the path is a submodule");
    }

    [Fact]
    public async Task RefusedResultsKeepTheRefAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json("{\"type\":\"submodule\"}"));

        RepoFileResponse response = await this.ReadAsync(upstream: upstream, gitRef: SHA);

        Assert.Equal(expected: RepoFileKind.Refused, actual: response.Kind);
        Assert.Equal(expected: SHA, actual: response.Ref);
    }

    [Fact]
    public async Task InvalidBase64FailsTheReadAsync()
    {
        RepoFileFailedException exception = await this.ReadFailsAsync(
            FileJsonOf(size: "5", encoding: "\"base64\"", content: "\"not*base64\"")
        );

        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.IsType<FormatException>(inner.InnerException);
    }

    [Theory]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"dir\"}")]
    [InlineData("{\"type\":\"dir\",\"entries\":[{\"type\":\"file\"}]}")]
    [InlineData("{\"type\":\"dir\",\"entries\":[{\"name\":\"a\"}]}")]
    [InlineData("{\"type\":\"dir\",\"entries\":[null]}")]
    public async Task MalformedContentFailsTheReadAsync(string json)
    {
        RepoFileFailedException exception = await this.ReadFailsAsync(json);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task UpstreamErrorFailsTheReadAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(status));

        RepoFileFailedException exception = await Assert.ThrowsAsync<RepoFileFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/name/extra")]
    [InlineData("")]
    public Task InvalidRepositoryIsRefusedWithoutRequestAsync(string repository)
    {
        return this.AssertInvalidWithoutRequestAsync(parameterName: "repository", repository: repository);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/README.md")]
    [InlineData("docs/")]
    [InlineData("a//b")]
    [InlineData("../secret")]
    [InlineData("a/./b")]
    [InlineData("a\nb")]
    public Task InvalidPathIsRefusedWithoutRequestAsync(string path)
    {
        return this.AssertInvalidWithoutRequestAsync(parameterName: "path", path: path);
    }

    [Fact]
    public Task OverlongPathIsRefusedWithoutRequestAsync()
    {
        return this.AssertInvalidWithoutRequestAsync(parameterName: "path", path: new('a', count: 4097));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("main.lock")]
    public Task InvalidRefIsRefusedWithoutRequestAsync(string gitRef)
    {
        return this.AssertInvalidWithoutRequestAsync(parameterName: "gitRef", gitRef: gitRef);
    }

    private async Task AssertInvalidWithoutRequestAsync(
        string parameterName,
        string repository = REPOSITORY,
        string path = PATH,
        string? gitRef = null
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, repository: repository, path: path, gitRef: gitRef)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
