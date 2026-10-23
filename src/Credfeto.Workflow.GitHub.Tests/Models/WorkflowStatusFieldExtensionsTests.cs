using System;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Models;

public sealed class WorkflowStatusFieldExtensionsTests : TestBase
{
    private readonly WorkflowStatusField _statusField = new(
        FieldId: "PVTSSF_status",
        FieldName: "Status",
        OptionIds: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["In Progress"] = "OPT_progress",
            ["Done"] = "OPT_done",
            ["DONE"] = "OPT_done_upper",
        }
    );

    [Theory]
    [InlineData("In Progress", "OPT_progress")]
    [InlineData("Done", "OPT_done")]
    [InlineData("DONE", "OPT_done_upper")]
    public void ExactNameMatchWins(string name, string expected)
    {
        Assert.Equal(expected: expected, actual: this._statusField.FindOptionId(name));
    }

    [Fact]
    public void UniqueCaseInsensitiveMatchIsFound()
    {
        Assert.Equal(expected: "OPT_progress", actual: this._statusField.FindOptionId("in progress"));
    }

    [Fact]
    public void UnknownNameIsNotFound()
    {
        Assert.Null(this._statusField.FindOptionId("Backlog"));
    }

    [Fact]
    public void AmbiguousCaseInsensitiveMatchIsReported()
    {
        AmbiguousStatusOptionException exception = Assert.Throws<AmbiguousStatusOptionException>(() =>
            this._statusField.FindOptionId("done")
        );

        Assert.Contains(
            expectedSubstring: "done",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
    }
}
