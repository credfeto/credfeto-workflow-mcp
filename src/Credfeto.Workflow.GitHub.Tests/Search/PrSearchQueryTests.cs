using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class PrSearchQueryTests : TestBase
{
    private const string STATE = "state";

    [Theory]
    [InlineData(null, PrSearchState.OPEN)]
    [InlineData("", PrSearchState.OPEN)]
    [InlineData("open", PrSearchState.OPEN)]
    [InlineData("closed", PrSearchState.CLOSED)]
    [InlineData("merged", PrSearchState.MERGED)]
    [InlineData("all", PrSearchState.ALL)]
    public static void ValidStateIsAccepted(string? value, PrSearchState expected)
    {
        Assert.Equal(expected: expected, actual: PrSearchQuery.State(value: value, parameterName: STATE));
    }

    [Theory]
    [InlineData("OPEN")]
    [InlineData("draft")]
    [InlineData(" merged")]
    public static void InvalidStateIsRefused(string value)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            PrSearchQuery.State(value: value, parameterName: STATE)
        );

        Assert.Equal(expected: STATE, actual: exception.ParameterName);
        Assert.Equal(expected: "it must be open, closed, merged or all", actual: exception.Rule);
    }

    [Theory]
    [InlineData(PrSearchState.OPEN, "in:title,body crash repo:owner/name type:pr state:open")]
    [InlineData(PrSearchState.CLOSED, "in:title,body crash repo:owner/name type:pr state:closed")]
    [InlineData(PrSearchState.MERGED, "in:title,body crash repo:owner/name type:pr is:merged")]
    [InlineData(PrSearchState.ALL, "in:title,body crash repo:owner/name type:pr")]
    public static void QueryAlwaysHasTheRepositoryAndType(PrSearchState state, string expected)
    {
        Assert.Equal(
            expected: expected,
            actual: PrSearchQuery.Build(repository: "owner/name", words: "crash", state: state)
        );
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner/name/extra")]
    public static void InvalidRepositoryIsRefused(string repository)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            PrSearchQuery.Build(repository: repository, words: "crash", state: PrSearchState.OPEN)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }
}
