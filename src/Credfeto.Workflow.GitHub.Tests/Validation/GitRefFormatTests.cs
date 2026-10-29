using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Validation;

public sealed class GitRefFormatTests : TestBase
{
    [Theory]
    [InlineData("main")]
    [InlineData("feature/my-branch")]
    [InlineData("v1.2.3")]
    [InlineData("release/2026.10")]
    [InlineData("a@b")]
    [InlineData("HEAD")]
    [InlineData("-leading-dash")]
    [InlineData("lock")]
    [InlineData("x.lockfile")]
    public static void ValidRefNamesAreAccepted(string name)
    {
        Assert.True(GitRefFormat.IsValidRefName(name), userMessage: name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("a..b")]
    [InlineData("a@{b")]
    [InlineData("a//b")]
    [InlineData("a b")]
    [InlineData("a~b")]
    [InlineData("a^b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a[b")]
    [InlineData("a\\b")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    [InlineData("a\u007Fb")]
    [InlineData("a\u0085b")]
    [InlineData("a\u009Fb")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("trailing.")]
    [InlineData(".hidden")]
    [InlineData("feature/.hidden")]
    [InlineData("name.lock")]
    [InlineData("name.lock/child")]
    public static void InvalidRefNamesAreRejected(string name)
    {
        Assert.False(GitRefFormat.IsValidRefName(name), userMessage: name);
    }

    [Theory]
    [InlineData('\u0000', true)]
    [InlineData('\u001F', true)]
    [InlineData(' ', false)]
    [InlineData('~', false)]
    [InlineData('\u007F', true)]
    [InlineData('\u0080', true)]
    [InlineData('\u009F', true)]
    [InlineData(' ', false)]
    public static void ControlCharactersAreDetected(char value, bool expected)
    {
        Assert.Equal(expected: expected, actual: GitRefFormat.IsControlCharacter(value));
    }

    [Theory]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    [InlineData("tab\there", true)]
    [InlineData("c1\u0085here", true)]
    public static void ContainsControlCharacterScansWholeString(string value, bool expected)
    {
        Assert.Equal(expected: expected, actual: GitRefFormat.ContainsControlCharacter(value));
    }
}
