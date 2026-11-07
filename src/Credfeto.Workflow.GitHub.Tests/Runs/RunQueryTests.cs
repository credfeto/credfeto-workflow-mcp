using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Runs;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunQueryTests : TestBase
{
    private const string PARAMETER = "value";

    [Theory]
    [InlineData(1)]
    [InlineData(RunQuery.DEFAULT_LIMIT)]
    [InlineData(RunQuery.MAX_LIMIT)]
    public static void LimitInRangeIsAccepted(int limit)
    {
        Assert.Equal(expected: limit, actual: RunQuery.Limit(value: limit, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RunQuery.MAX_LIMIT + 1)]
    [InlineData(int.MaxValue)]
    public static void LimitOutOfRangeIsRejected(int limit)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Limit(value: limit, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a whole number from 1 to 50", actual: exception.Rule);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(RunQuery.DEFAULT_LINES)]
    [InlineData(RunQuery.MAX_LINES)]
    public static void LinesInRangeIsAccepted(int lines)
    {
        Assert.Equal(expected: lines, actual: RunQuery.Lines(value: lines, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(RunQuery.MAX_LINES + 1)]
    public static void LinesOutOfRangeIsRejected(int lines)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Lines(value: lines, parameterName: PARAMETER)
        );

        Assert.Equal(expected: "it must be a whole number from 1 to 2000", actual: exception.Rule);
    }

    [Theory]
    [InlineData("build.yml")]
    [InlineData("Build and test")]
    [InlineData("a-b")]
    public static void ValidWorkflowIsAccepted(string workflow)
    {
        Assert.Equal(expected: workflow, actual: RunQuery.Workflow(value: workflow, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-build.yml")]
    [InlineData("build\nyml")]
    [InlineData("build\u0000")]
    public static void InvalidWorkflowIsRejected(string workflow)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Workflow(value: workflow, parameterName: PARAMETER)
        );

        Assert.Equal(
            expected: "it must not be empty, start with '-' or contain control characters",
            actual: exception.Rule
        );
    }

    [Fact]
    public void WorkflowOfTheMaximumLengthIsAccepted()
    {
        string workflow = new('w', 256);

        Assert.Equal(expected: workflow, actual: RunQuery.Workflow(value: workflow, parameterName: PARAMETER));
    }

    [Fact]
    public void WorkflowLengthCountsRunes()
    {
        // Each emoji is two UTF-16 code units but one rune, so this is 256 runes in 512 code units
        string workflow = string.Concat(System.Linq.Enumerable.Repeat(element: "\U0001F680", count: 256));

        Assert.Equal(expected: workflow, actual: RunQuery.Workflow(value: workflow, parameterName: PARAMETER));
    }

    [Fact]
    public void LongWorkflowIsRejected()
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Workflow(value: new string('w', 257), parameterName: PARAMETER)
        );

        Assert.Equal(expected: "it must be at most 256 characters long", actual: exception.Rule);
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("0012", 12L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public static void IdIsParsed(string value, long expected)
    {
        Assert.Equal(expected: expected, actual: RunQuery.Id(value: value, parameterName: PARAMETER));
    }

    [Fact]
    public void IdTooLargeIsRejected()
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Id(value: "9223372036854775808", parameterName: PARAMETER)
        );

        Assert.Equal(expected: "it is too large", actual: exception.Rule);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1a")]
    [InlineData(" 1")]
    public static void InvalidIdIsRejected(string value)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Id(value: value, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
    }
}
