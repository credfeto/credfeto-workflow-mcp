using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueCreateResponseJsonTests : TestBase
{
    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/90";

    [Fact]
    public void AResponseRoundTrips()
    {
        IssueCreateResponse response = new(Url: new Uri(uriString: ISSUE_URL, uriKind: UriKind.Absolute), Note: null);

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse
        );

        Assert.Equal(expected: "{\"url\":\"" + ISSUE_URL + "\"}", actual: json);
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(json: json, jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse)
        );
    }

    [Fact]
    public void AResponseWithANoteRoundTrips()
    {
        IssueCreateResponse response = new(
            Url: new Uri(uriString: ISSUE_URL, uriKind: UriKind.Absolute),
            Note: "added with the board token"
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse
        );

        Assert.Equal(expected: "{\"url\":\"" + ISSUE_URL + "\",\"note\":\"added with the board token\"}", actual: json);
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(json: json, jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse)
        );
    }
}
