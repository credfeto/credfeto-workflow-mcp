using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Models;

public sealed class WorkflowStatesTests : TestBase
{
    [Fact]
    public static void AllStatesAreInBoardOrder()
    {
        Assert.Equal(
            expected:
            [
                "Not Started",
                "Planning",
                "Approved",
                "Development",
                "AI Simplify",
                "AI Review",
                "AI Security Review",
                "AI Coverage",
                "Human Review",
                "Complete",
            ],
            actual: WorkflowStates.All
        );
    }

    [Theory]
    [InlineData("AI Review", "AI Review")]
    [InlineData("ai review", "AI Review")]
    [InlineData("NOT STARTED", "Not Started")]
    [InlineData("complete", "Complete")]
    [InlineData("Human Review", "Human Review")]
    public static void StateIsCanonicalisedIgnoringCase(string state, string expected)
    {
        Assert.Equal(expected: expected, actual: WorkflowStates.Canonicalise(state));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Todo")]
    [InlineData("In Progress")]
    [InlineData("AIReview")]
    public static void UnknownStateListsEveryValidState(string state)
    {
        UnknownWorkflowStateException exception = Assert.Throws<UnknownWorkflowStateException>(() =>
            WorkflowStates.Canonicalise(state)
        );

        Assert.Equal(
            expected: "unknown --status (valid: Not Started, Planning, Approved, Development, AI Simplify, AI Review, AI Security Review, AI Coverage, Human Review, Complete)",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData("AI\nReview")]
    [InlineData("AI Review\t")]
    public static void MalformedStateIsRejected(string state)
    {
        Assert.Throws<ParameterValidationException>(() => WorkflowStates.Canonicalise(state));
    }
}
