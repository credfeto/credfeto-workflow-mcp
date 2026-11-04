using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class IssueSearchQueryTests : TestBase
{
    private const string STATE = "state";
    private const string LIMIT = "limit";
    private const string LIMIT_RULE = "it must be a whole number from 1 to 100";

    [Theory]
    [InlineData(null, IssueSearchState.OPEN)]
    [InlineData("", IssueSearchState.OPEN)]
    [InlineData("open", IssueSearchState.OPEN)]
    [InlineData("closed", IssueSearchState.CLOSED)]
    [InlineData("all", IssueSearchState.ALL)]
    public static void ValidStateIsAccepted(string? value, IssueSearchState expected)
    {
        Assert.Equal(expected: expected, actual: IssueSearchQuery.State(value: value, parameterName: STATE));
    }

    [Theory]
    [InlineData("OPEN")]
    [InlineData("merged")]
    [InlineData(" open")]
    public static void InvalidStateIsRefused(string value)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            IssueSearchQuery.State(value: value, parameterName: STATE)
        );

        Assert.Equal(expected: STATE, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be open, closed or all", actual: exception.Rule);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("007", 7)]
    [InlineData("0000000000000000000000100", 100)]
    public static void ValidLimitIsAccepted(string? value, int expected)
    {
        Assert.Equal(expected: expected, actual: IssueSearchQuery.Limit(value: value, parameterName: LIMIT));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("101")]
    [InlineData("999")]
    [InlineData("1000")]
    [InlineData("99999999999999999999999999")]
    public static void LimitOutOfRangeIsRefused(string value)
    {
        AssertLimitRefused(value: value, rule: LIMIT_RULE);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("ten")]
    public static void NonNumericLimitIsRefused(string value)
    {
        AssertLimitRefused(value: value, rule: "it must contain only the digits 0-9");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(100)]
    public static void ValidNumericLimitIsAccepted(int value)
    {
        Assert.Equal(expected: value, actual: IssueSearchQuery.Limit(value: value, parameterName: LIMIT));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public static void NumericLimitOutOfRangeIsRefused(int value)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            IssueSearchQuery.Limit(value: value, parameterName: LIMIT)
        );

        Assert.Equal(expected: LIMIT, actual: exception.ParameterName);
        Assert.Equal(expected: LIMIT_RULE, actual: exception.Rule);
    }

    [Theory]
    [InlineData(IssueSearchState.OPEN, "in:title,body crash repo:owner/name type:issue state:open")]
    [InlineData(IssueSearchState.CLOSED, "in:title,body crash repo:owner/name type:issue state:closed")]
    [InlineData(IssueSearchState.ALL, "in:title,body crash repo:owner/name type:issue")]
    public static void QueryAlwaysHasTheRepositoryAndType(IssueSearchState state, string expected)
    {
        Assert.Equal(
            expected: expected,
            actual: IssueSearchQuery.Build(repository: "owner/name", words: "crash", state: state)
        );
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner/name/extra")]
    [InlineData("owner/na me")]
    public static void InvalidRepositoryIsRefused(string repository)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            IssueSearchQuery.Build(repository: repository, words: "crash", state: IssueSearchState.OPEN)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }

    private static void AssertLimitRefused(string value, string rule)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            IssueSearchQuery.Limit(value: value, parameterName: LIMIT)
        );

        Assert.Equal(expected: LIMIT, actual: exception.ParameterName);
        Assert.Equal(expected: rule, actual: exception.Rule);
    }
}
