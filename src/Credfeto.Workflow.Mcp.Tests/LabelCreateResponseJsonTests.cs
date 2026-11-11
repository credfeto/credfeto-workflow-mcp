using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class LabelCreateResponseJsonTests : TestBase
{
    [Theory]
    [InlineData(LabelCreateOutcome.Created, "Created")]
    [InlineData(LabelCreateOutcome.Updated, "Updated")]
    [InlineData(LabelCreateOutcome.Unchanged, "Unchanged")]
    public static void TheOutcomeIsWrittenByName(LabelCreateOutcome outcome, string name)
    {
        LabelCreateResponse response = new(
            Outcome: outcome,
            Repository: "credfeto/credfeto-workflow-mcp",
            Name: "Bug",
            Colour: "d73a4a",
            Description: "Generic bug fix"
        );

        string actual = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.LabelCreateResponse
        );

        Assert.Equal(
            expected: "{\"outcome\":\""
                + name
                + "\",\"repository\":\"credfeto/credfeto-workflow-mcp\",\"name\":\"Bug\",\"colour\":\"d73a4a\",\"description\":\"Generic bug fix\"}",
            actual: actual
        );
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.LabelCreateResponse)
        );
    }
}
