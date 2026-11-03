using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Validation;

public sealed class StandardLabelsTests : TestBase
{
    [Theory]
    [InlineData("bug", "Bug", "d73a4a", "Generic bug fix")]
    [InlineData("ON HOLD", "On Hold", "ff0000", "Do not work on this")]
    [InlineData(".net update", ".NET update", "a870c9", "Update to .net global.json")]
    [InlineData("Medium", "Medium", "ffff00", "Medium Priority")]
    public static void AStandardLabelIsFoundWithItsColourAndDescription(
        string value,
        string name,
        string colour,
        string description
    )
    {
        Assert.Equal(
            expected: new StandardLabel(Name: name, Colour: colour, Description: description),
            actual: StandardLabels.FindDefinition(value)
        );
        Assert.Equal(expected: name, actual: StandardLabels.FindStandard(value));
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("credfeto-workflow-done")]
    [InlineData("Bug ")]
    public static void AnyOtherLabelIsNotStandard(string value)
    {
        Assert.Null(StandardLabels.FindDefinition(value));
        Assert.Null(StandardLabels.FindStandard(value));
    }

    [Theory]
    [InlineData("security", "Security")]
    [InlineData("URGENT", "Urgent")]
    [InlineData("High", "High")]
    [InlineData("medium", "Medium")]
    [InlineData("low", "Low")]
    public static void APriorityIsFoundInItsCanonicalSpelling(string value, string expected)
    {
        Assert.Equal(expected: expected, actual: StandardLabels.FindPriority(value));
        Assert.True(condition: StandardLabels.IsPriority(value), userMessage: value);
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("Bug")]
    [InlineData("")]
    public static void AnyOtherValueIsNotAPriority(string value)
    {
        Assert.Null(StandardLabels.FindPriority(value));
        Assert.False(condition: StandardLabels.IsPriority(value), userMessage: value);
    }

    [Fact]
    public static void ThePrioritiesAreListedMostUrgentFirst()
    {
        Assert.Equal(expected: "Security, Urgent, High, Medium or Low", actual: StandardLabels.PriorityList);
    }
}
