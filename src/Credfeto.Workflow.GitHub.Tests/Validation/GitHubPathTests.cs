using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Validation;

public sealed class GitHubPathTests : TestBase
{
    [Theory]
    [InlineData("main", "main")]
    [InlineData("feature/my-branch", "feature/my-branch")]
    [InlineData("AZaz09._~-/", "AZaz09._~-/")]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("a@b", "a%40b")]
    [InlineData("100%", "100%25")]
    [InlineData("a#b", "a%23b")]
    [InlineData("a?b", "a%3Fb")]
    [InlineData("a b", "a%20b")]
    [InlineData("a^b", "a%5Eb")]
    [InlineData("a`b", "a%60b")]
    [InlineData("a{b", "a%7Bb")]
    [InlineData("a\u007Fb", "a%7Fb")]
    [InlineData("café", "caf%C3%A9")]
    [InlineData("", "")]
    public static void BranchPathEncodesReservedCharacters(string branch, string expected)
    {
        Assert.Equal(expected: expected, actual: GitHubPath.BranchPath(branch));
    }
}
