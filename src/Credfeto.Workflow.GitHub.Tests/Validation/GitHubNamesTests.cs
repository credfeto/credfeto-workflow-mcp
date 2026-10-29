using System;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Validation;

public sealed class GitHubNamesTests : TestBase
{
    private const string PARAMETER = "param";

    [Theory]
    [InlineData("credfeto/credfeto-workflow-mcp")]
    [InlineData("Owner_1/repo.name")]
    [InlineData("a/b")]
    [InlineData("owner/.github")]
    public static void RepositoryAcceptsOwnerSlashRepo(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Repository(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("/repo")]
    [InlineData("owner/")]
    [InlineData("owner/re po")]
    [InlineData("ow:ner/repo")]
    [InlineData("./repo")]
    [InlineData("owner/..")]
    [InlineData("owner/repo\n")]
    public static void RepositoryRejectsInvalidValues(string value)
    {
        AssertRejected(() => GitHubNames.Repository(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("000", "0")]
    [InlineData("42", "42")]
    [InlineData("0042", "42")]
    public static void NumberNormalisesLeadingZeros(string value, string expected)
    {
        Assert.Equal(expected: expected, actual: GitHubNames.Number(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.5")]
    [InlineData(" 1")]
    [InlineData("1\n")]
    [InlineData("١")]
    public static void NumberRejectsNonDigits(string value)
    {
        AssertRejected(() => GitHubNames.Number(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("007", "7")]
    public static void PositiveNumberAcceptsNonZero(string value, string expected)
    {
        Assert.Equal(expected: expected, actual: GitHubNames.PositiveNumber(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0000")]
    [InlineData("")]
    [InlineData("x")]
    public static void PositiveNumberRejectsZeroAndNonDigits(string value)
    {
        AssertRejected(() => GitHubNames.PositiveNumber(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("1234567890123456789", "1234567890123456789")]
    [InlineData("0001234567890123456789", "1234567890123456789")]
    [InlineData("5", "5")]
    public static void CommentIdAcceptsUpToNineteenSignificantDigits(string value, string expected)
    {
        Assert.Equal(expected: expected, actual: GitHubNames.CommentId(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("12345678901234567890")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("12a")]
    public static void CommentIdRejectsInvalidValues(string value)
    {
        AssertRejected(() => GitHubNames.CommentId(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567", "0123456789abcdef0123456789abcdef01234567")]
    public static void CommitShaIsNormalisedToLowerCase(string value, string expected)
    {
        Assert.Equal(expected: expected, actual: GitHubNames.CommitSha(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData("0123456789abcdef0123456789abcdef0123456\n")]
    public static void CommitShaRejectsInvalidValues(string value)
    {
        AssertRejected(() => GitHubNames.CommitSha(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("feature/11-name-validation")]
    [InlineData("head")]
    [InlineData("a-b")]
    public static void BranchAcceptsValidNames(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Branch(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-x")]
    [InlineData("HEAD")]
    [InlineData("a..b")]
    [InlineData("a b")]
    [InlineData("a\u0001b")]
    [InlineData("a\u0085b")]
    [InlineData("x.lock")]
    public static void BranchRejectsInvalidNames(string value)
    {
        AssertRejected(() => GitHubNames.Branch(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("-x")]
    [InlineData("HEAD")]
    public static void TagAcceptsValidNames(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Tag(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1..0")]
    [InlineData("v1\u0001")]
    [InlineData("v1\u0085")]
    public static void TagRejectsInvalidNames(string value)
    {
        AssertRejected(() => GitHubNames.Tag(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("In Progress")]
    [InlineData("Done ✔")]
    public static void StatusAcceptsTextWithoutControlCharacters(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Status(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("In\nProgress")]
    [InlineData("Done\u0085")]
    public static void StatusRejectsControlCharacters(string value)
    {
        AssertRejected(() => GitHubNames.Status(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("Type: Feature")]
    [InlineData("12345678901234567890123456789012345678901234567890")]
    public static void LabelAcceptsValidValues(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Label(value: value, parameterName: PARAMETER));
    }

    [Fact]
    public void LabelCountsEmojiAsOneCharacter()
    {
        string value = string.Concat(Repeat(count: 50, text: "\U0001F600"));

        Assert.Equal(expected: value, actual: GitHubNames.Label(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a,b")]
    [InlineData("a\tb")]
    [InlineData("a\u0085b")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public static void LabelRejectsInvalidValues(string value)
    {
        AssertRejected(() => GitHubNames.Label(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("dependabot[bot]")]
    [InlineData("user_name-1")]
    public static void LoginAcceptsValidValues(string value)
    {
        Assert.Equal(expected: value, actual: GitHubNames.Login(value: value, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[bot]")]
    [InlineData("user name")]
    [InlineData("user[bot]x")]
    [InlineData("user.name")]
    [InlineData("user\n")]
    public static void LoginRejectsInvalidValues(string value)
    {
        AssertRejected(() => GitHubNames.Login(value: value, parameterName: PARAMETER));
    }

    [Fact]
    public void RejectionMessageDoesNotEchoTheValue()
    {
        const string hostile = "<script>evil</script>/repo";

        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            GitHubNames.Repository(value: hostile, parameterName: PARAMETER)
        );

        Assert.DoesNotContain(
            expectedSubstring: "evil",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    private static string[] Repeat(int count, string text)
    {
        string[] items = new string[count];
        Array.Fill(array: items, value: text);

        return items;
    }

    // The message is fully determined by the parameter name and rule, which proves the rejected value is never echoed
    private static void AssertRejected(Func<string> validate)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(validate);

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.NotEmpty(exception.Rule);
        Assert.Equal(expected: $"Parameter '{PARAMETER}' is not valid: {exception.Rule}", actual: exception.Message);
    }
}
