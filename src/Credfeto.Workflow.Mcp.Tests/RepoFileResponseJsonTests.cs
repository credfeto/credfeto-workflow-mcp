using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RepoFileResponseJsonTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const string FILE_JSON =
        "{\"kind\":\"File\",\"message\":\"the file was read\",\"repository\":\"credfeto/credfeto-workflow-mcp\",\"path\":\"README.md\",\"ref\":\"main\",\"size\":5,\"content\":\"hello\"}";

    private const string DIRECTORY_JSON =
        "{\"kind\":\"Directory\",\"message\":\"the directory was listed\",\"repository\":\"credfeto/credfeto-workflow-mcp\",\"path\":\"docs\",\"entries\":[{\"type\":\"dir\",\"name\":\"src\"}]}";

    private const string NOT_FOUND_JSON =
        "{\"kind\":\"NotFound\",\"message\":\"not found\",\"repository\":\"credfeto/credfeto-workflow-mcp\"}";

    [Fact]
    public static void AFileSerialisesWithItsContentAndSize()
    {
        RepoFileResponse response = new(
            Kind: RepoFileKind.File,
            Message: "the file was read",
            Repository: REPOSITORY,
            Path: "README.md",
            Ref: "main",
            Size: 5,
            Content: "hello",
            Entries: null
        );

        Assert.Equal(expected: FILE_JSON, actual: Serialise(response));
    }

    [Fact]
    public static void ADirectorySerialisesWithItsEntries()
    {
        RepoFileResponse response = new(
            Kind: RepoFileKind.Directory,
            Message: "the directory was listed",
            Repository: REPOSITORY,
            Path: "docs",
            Ref: null,
            Size: null,
            Content: null,
            Entries: [new RepoFileEntry(Type: "dir", Name: "src")]
        );

        Assert.Equal(expected: DIRECTORY_JSON, actual: Serialise(response));
    }

    [Fact]
    public static void NotFoundSerialisesWithoutPathOrRef()
    {
        RepoFileResponse response = new(
            Kind: RepoFileKind.NotFound,
            Message: "not found",
            Repository: REPOSITORY,
            Path: null,
            Ref: null,
            Size: null,
            Content: null,
            Entries: null
        );

        Assert.Equal(expected: NOT_FOUND_JSON, actual: Serialise(response));
    }

    [Theory]
    [InlineData(FILE_JSON)]
    [InlineData(DIRECTORY_JSON)]
    [InlineData(NOT_FOUND_JSON)]
    public static void AResponseRoundTrips(string json)
    {
        RepoFileResponse? response = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.RepoFileResponse
        );

        Assert.NotNull(response);
        Assert.Equal(expected: json, actual: Serialise(response));
    }

    private static string Serialise(RepoFileResponse response)
    {
        return JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.RepoFileResponse);
    }
}
