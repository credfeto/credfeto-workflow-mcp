using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueAddSubIssueResponseJsonTests : TestBase
{
    [Fact]
    public void AResponseRoundTrips()
    {
        IssueAddSubIssueResponse response = new(
            Parent: "credfeto/credfeto-workflow-mcp#16",
            SubIssue: "credfeto/credfeto-workflow-mcp#17",
            Outcome: "linked",
            ParentUrl: new(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/16",
                uriKind: UriKind.Absolute
            ),
            SubIssueUrl: new(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/17",
                uriKind: UriKind.Absolute
            )
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.IssueAddSubIssueResponse
        );

        Assert.Equal(
            expected: "{\"parent\":\"credfeto/credfeto-workflow-mcp#16\",\"subIssue\":\"credfeto/credfeto-workflow-mcp#17\",\"outcome\":\"linked\",\"parentUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/16\",\"subIssueUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/17\"}",
            actual: json
        );
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(
                json: json,
                jsonTypeInfo: AppJsonContext.Default.IssueAddSubIssueResponse
            )
        );
    }
}
