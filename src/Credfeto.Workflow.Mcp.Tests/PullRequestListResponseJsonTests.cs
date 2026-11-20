using System;
using System.Collections.Generic;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PullRequestListResponseJsonTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const string ENTRY =
        "{\"number\":7,\"head\":\"feat/7-x\",\"base\":\"main\",\"author\":\"credfeto\",\"state\":\"ready\",\"flags\":[],"
        + "\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/7\",\"title\":\"A change\",\"labels\":[\"bug\"]";

    private static PullRequestListResponse Response(IReadOnlyList<ClosingIssueReference>? closingIssues)
    {
        return new(
            Repository: REPOSITORY,
            PullRequests:
            [
                new(
                    Number: 7,
                    Head: "feat/7-x",
                    Base: "main",
                    Author: "credfeto",
                    State: "ready",
                    Flags: [],
                    Url: new Uri("https://github.com/" + REPOSITORY + "/pull/7"),
                    Title: "A change",
                    Labels: ["bug"],
                    ClosingIssues: closingIssues
                ),
            ]
        );
    }

    private static string Serialise(PullRequestListResponse response)
    {
        return JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PullRequestListResponse);
    }

    [Fact]
    public void ClosingIssuesAreLeftOutWhenNotRead()
    {
        Assert.Equal(
            expected: "{\"repository\":\"" + REPOSITORY + "\",\"pullRequests\":[" + ENTRY + "}]}",
            actual: Serialise(Response(null))
        );
    }

    [Fact]
    public void ClosingIssuesAreListedWithTheirRepository()
    {
        Assert.Equal(
            expected: "{\"repository\":\""
                + REPOSITORY
                + "\",\"pullRequests\":["
                + ENTRY
                + ",\"closingIssues\":[{\"repository\":\""
                + REPOSITORY
                + "\",\"number\":12}]}]}",
            actual: Serialise(Response([new(Repository: REPOSITORY, Number: 12)]))
        );
    }
}
