using System;
using System.Text.RegularExpressions;
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
    [InlineData(0)]
    [InlineData(RunQuery.MAX_CONTEXT)]
    public static void ContextInRangeIsAccepted(int context)
    {
        Assert.Equal(expected: context, actual: RunQuery.Context(value: context, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(RunQuery.MAX_CONTEXT + 1)]
    public static void ContextOutOfRangeIsRejected(int context)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Context(value: context, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a whole number from 0 to 20", actual: exception.Rule);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(RunQuery.DEFAULT_MAX_MATCHES)]
    [InlineData(RunQuery.MAX_MAX_MATCHES)]
    public static void MaxMatchesInRangeIsAccepted(int maxMatches)
    {
        Assert.Equal(expected: maxMatches, actual: RunQuery.MaxMatches(value: maxMatches, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RunQuery.MAX_MAX_MATCHES + 1)]
    public static void MaxMatchesOutOfRangeIsRejected(int maxMatches)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.MaxMatches(value: maxMatches, parameterName: PARAMETER)
        );

        Assert.Equal(expected: "it must be a whole number from 1 to 200", actual: exception.Rule);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public static void FromLineOfAtLeastOneIsAccepted(int fromLine)
    {
        Assert.Equal(expected: fromLine, actual: RunQuery.FromLine(value: fromLine, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public static void FromLineBelowOneIsRejected(int fromLine)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.FromLine(value: fromLine, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a whole number of at least 1", actual: exception.Rule);
    }

    [Fact]
    public void GrepRunsWithoutBacktracking()
    {
        Regex grep = RunQuery.Grep(value: "not ok [0-9]+", parameterName: PARAMETER);

        Assert.Equal(expected: RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, actual: grep.Options);
        Assert.Matches(expectedRegex: grep, actualString: "not ok 12");
    }

    [Fact]
    public void GrepThatNeedsBacktrackingHasATimeout()
    {
        Regex grep = RunQuery.Grep(value: "(a)\\1", parameterName: PARAMETER);

        Assert.Equal(expected: RegexOptions.CultureInvariant, actual: grep.Options);
        Assert.Equal(expected: TimeSpan.FromSeconds(1), actual: grep.MatchTimeout);
        Assert.Matches(expectedRegex: grep, actualString: "aa");
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    public static void InvalidGrepIsRejected(string grep)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Grep(value: grep, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a valid regular expression", actual: exception.Rule);
    }

    [Fact]
    public void GrepOfTheMaximumLengthIsAccepted()
    {
        Regex grep = RunQuery.Grep(value: new string('g', RunQuery.MAX_GREP_LENGTH), parameterName: PARAMETER);

        Assert.Matches(expectedRegex: grep, actualString: new string('g', RunQuery.MAX_GREP_LENGTH));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RunQuery.MAX_GREP_LENGTH + 1)]
    public static void GrepOfTheWrongLengthIsRejected(int length)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RunQuery.Grep(value: new string('g', length), parameterName: PARAMETER)
        );

        Assert.Equal(expected: "it must be from 1 to 256 characters long", actual: exception.Rule);
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
