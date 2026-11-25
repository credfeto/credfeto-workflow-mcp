using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssuePostPlanResponseJsonTests : TestBase
{
    private const string COMMENT_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/173#issuecomment-1";

    [Theory]
    [InlineData("Planning")]
    [InlineData("no board")]
    public static void AResponseSerialisesEveryStep(string board)
    {
        IssuePostPlanResponse response = new(
            CommentUrl: new Uri(uriString: COMMENT_URL, uriKind: UriKind.Absolute),
            Labels: ["Blocked"],
            Board: board
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.IssuePostPlanResponse
        );

        Assert.Equal(
            expected: "{\"commentUrl\":\"" + COMMENT_URL + "\",\"labels\":[\"Blocked\"],\"board\":\"" + board + "\"}",
            actual: json
        );

        IssuePostPlanResponse? actual = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.IssuePostPlanResponse
        );

        Assert.NotNull(actual);
        Assert.Equal(expected: response.CommentUrl, actual: actual.CommentUrl);
        Assert.Equal(expected: response.Labels, actual: actual.Labels);
        Assert.Equal(expected: response.Board, actual: actual.Board);
    }
}
