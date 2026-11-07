using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class RepoSearchQueryTests : TestBase
{
    [Theory]
    [InlineData(false, "in:name,description crash user:credfeto archived:false")]
    [InlineData(true, "in:name,description crash user:credfeto")]
    public static void QueryAlwaysHasTheOwner(bool includeArchived, string expected)
    {
        Assert.Equal(
            expected: expected,
            actual: RepoSearchQuery.Build(owner: "credfeto", words: "crash", includeArchived: includeArchived)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("-credfeto")]
    [InlineData("credfeto/name")]
    public static void InvalidOwnerIsRefused(string owner)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            RepoSearchQuery.Build(owner: owner, words: "crash", includeArchived: false)
        );

        Assert.Equal(expected: "owner", actual: exception.ParameterName);
    }
}
