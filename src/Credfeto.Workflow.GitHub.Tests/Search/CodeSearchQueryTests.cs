using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class CodeSearchQueryTests : TestBase
{
    [Fact]
    public static void QueryAlwaysHasTheRepository()
    {
        Assert.Equal(
            expected: "crash repo:owner/name",
            actual: CodeSearchQuery.Build(repository: "owner/name", words: "crash")
        );
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner/name/extra")]
    public static void InvalidRepositoryIsRefused(string repository)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            CodeSearchQuery.Build(repository: repository, words: "crash")
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }
}
