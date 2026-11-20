using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueViewResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"issue\":\"credfeto/credfeto-workflow-mcp#150\",\"title\":\"Fingerprint input\",\"state\":\"OPEN\",\"labels\":[\"AI-Work\"],\"assignees\":[\"credfeto\"],\"body\":\"Body\",\"milestone\":\"Phase 2\",\"comments\":[{\"id\":\"1\",\"author\":\"app/github-actions\",\"createdAt\":\"2026-01-02T03:04:05Z\",\"updatedAt\":\"2026-01-03T04:05:06Z\",\"body\":\"line one\\r\\nline two\\n\"}]}";

    private const string PLAIN_JSON =
        "{\"issue\":\"credfeto/credfeto-workflow-mcp#150\",\"title\":\"Fingerprint input\",\"state\":\"OPEN\",\"labels\":[],\"assignees\":[],\"body\":\"\"}";

    [Fact]
    public void AViewWithMilestoneAndCommentsSerialises()
    {
        IssueViewResponse view = new(
            Issue: "credfeto/credfeto-workflow-mcp#150",
            Title: "Fingerprint input",
            State: "OPEN",
            Labels: ["AI-Work"],
            Assignees: ["credfeto"],
            Body: "Body",
            Milestone: "Phase 2",
            Comments:
            [
                new IssueViewComment(
                    Id: "1",
                    Author: "app/github-actions",
                    CreatedAt: "2026-01-02T03:04:05Z",
                    UpdatedAt: "2026-01-03T04:05:06Z",
                    Body: "line one\r\nline two\n"
                ),
            ]
        );

        string json = JsonSerializer.Serialize(value: view, jsonTypeInfo: AppJsonContext.Default.IssueViewResponse);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AViewWithoutMilestoneOrCommentsLeavesThemOut()
    {
        IssueViewResponse view = new(
            Issue: "credfeto/credfeto-workflow-mcp#150",
            Title: "Fingerprint input",
            State: "OPEN",
            Labels: [],
            Assignees: [],
            Body: string.Empty
        );

        string json = JsonSerializer.Serialize(value: view, jsonTypeInfo: AppJsonContext.Default.IssueViewResponse);

        Assert.Equal(expected: PLAIN_JSON, actual: json);
    }
}
