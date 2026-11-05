using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssuePlanStatusJsonTests : TestBase
{
    private const string JSON =
        "{\"hasPlan\":true,\"blocked\":false,\"planAt\":\"2026-01-02T03:04:05Z\",\"planBy\":{\"login\":\"credfeto\",\"association\":\"OWNER\",\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/17#issuecomment-1\"},\"comments\":[{\"createdAt\":\"2026-01-03T03:04:05Z\",\"login\":\"reviewer\",\"association\":\"MEMBER\",\"hasApprovalKeyword\":true,\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/17#issuecomment-2\"}]}";

    private const string NO_PLAN_JSON = "{\"hasPlan\":false,\"blocked\":true,\"comments\":[]}";

    [Fact]
    public void AStatusWithAPlanSerialises()
    {
        IssuePlanStatus status = new(
            HasPlan: true,
            Blocked: false,
            PlanAt: "2026-01-02T03:04:05Z",
            PlanBy: new(
                Login: "credfeto",
                Association: "OWNER",
                Url: new("https://github.com/credfeto/credfeto-workflow-mcp/issues/17#issuecomment-1", UriKind.Absolute)
            ),
            Comments:
            [
                new PlanFollowUpComment(
                    CreatedAt: "2026-01-03T03:04:05Z",
                    Login: "reviewer",
                    Association: "MEMBER",
                    HasApprovalKeyword: true,
                    Url: new(
                        "https://github.com/credfeto/credfeto-workflow-mcp/issues/17#issuecomment-2",
                        UriKind.Absolute
                    )
                ),
            ]
        );

        string json = JsonSerializer.Serialize(value: status, jsonTypeInfo: AppJsonContext.Default.IssuePlanStatus);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AStatusWithoutAPlanOmitsThePlanFields()
    {
        IssuePlanStatus status = new(HasPlan: false, Blocked: true, PlanAt: null, PlanBy: null, Comments: []);

        string json = JsonSerializer.Serialize(value: status, jsonTypeInfo: AppJsonContext.Default.IssuePlanStatus);

        Assert.Equal(expected: NO_PLAN_JSON, actual: json);
    }

    [Fact]
    public void AStatusRoundTrips()
    {
        IssuePlanStatus? status = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.IssuePlanStatus
        );

        Assert.NotNull(status);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: status, jsonTypeInfo: AppJsonContext.Default.IssuePlanStatus)
        );
    }
}
