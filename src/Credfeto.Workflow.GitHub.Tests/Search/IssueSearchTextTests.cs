using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection
public sealed class IssueSearchTextTests : TestBase
{
    private const string PARAMETER = "text";
    private const string SECRET = "gh" + "p_abcdef";

    [Theory]
    [InlineData("crash on start", "crash on start")]
    [InlineData("repo:other/name crash", "repo other name crash")]
    [InlineData("\"exact phrase\"", "exact phrase")]
    [InlineData("(a OR-tools) [b] {c}", "a OR-tools b c")]
    [InlineData("  many    spaces  ", "many spaces")]
    [InlineData("keep _ . # ' - these", "keep _ . # ' - these")]
    [InlineData("and or not", "and or not")]
    [InlineData("And Or Not", "And Or Not")]
    [InlineData("ANDROID ORACLE NOTE", "ANDROID ORACLE NOTE")]
    [InlineData("OR-tools", "OR-tools")]
    public static void ValidTextIsSanitised(string text, string expected)
    {
        Assert.Equal(expected: expected, actual: IssueSearchText.Words(text: text, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("café")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("delete\u007f")]
    public static void NonPrintableAsciiIsRefused(string text)
    {
        AssertUsage(text: text, rule: "it must contain only printable ASCII characters");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":-\"()")]
    public static void TextWithoutLetterOrDigitIsRefused(string text)
    {
        AssertUsage(text: text, rule: "it must contain a letter or digit");
    }

    [Theory]
    [InlineData("AND")]
    [InlineData("a OR b")]
    [InlineData("a NOT")]
    [InlineData("-NOT b")]
    [InlineData("a -AND b")]
    [InlineData("a:OR:b")]
    public static void UpperCaseOperatorIsRefused(string text)
    {
        AssertUsage(text: text, rule: "it must not contain the search operators AND, OR or NOT");
    }

    [Fact]
    public void WordsAtTheMaximumLengthPass()
    {
        string text = new('a', count: IssueSearchText.MAX_WORDS_LENGTH);

        Assert.Equal(expected: text, actual: IssueSearchText.Words(text: text, parameterName: PARAMETER));
    }

    [Fact]
    public void WordsOverTheMaximumLengthAreRefused()
    {
        AssertUsage(text: new('a', count: IssueSearchText.MAX_WORDS_LENGTH + 1), rule: "it is too long");
    }

    [Fact]
    public void LengthIsMeasuredAfterSanitising()
    {
        string text = "a" + new string(' ', count: IssueSearchText.MAX_WORDS_LENGTH) + "b";

        Assert.Equal(expected: "a b", actual: IssueSearchText.Words(text: text, parameterName: PARAMETER));
    }

    [Theory]
    [InlineData("token " + SECRET)]
    [InlineData("TOKEN=abc")]
    public static void TokenInGivenTextIsRefused(string text)
    {
        AssertRefused(text: text, rule: "token");
    }

    [Fact]
    public void TokenFormedBySanitisingIsRefused()
    {
        AssertRefused(text: "Bearer:abcdefghijklmnopqrs1", rule: "token");
    }

    [Fact]
    public void HostPathIsRefused()
    {
        AssertRefused(text: "see /home/user/notes", rule: "host path");
    }

    private static void AssertUsage(string text, string rule)
    {
        ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
            IssueSearchText.Words(text: text, parameterName: PARAMETER)
        );

        Assert.Equal(expected: PARAMETER, actual: exception.ParameterName);
        Assert.Equal(expected: rule, actual: exception.Rule);

        // The exact message proves the value is never echoed back to the caller
        Assert.Equal(expected: "Parameter '" + PARAMETER + "' is not valid: " + rule, actual: exception.Message);
    }

    private static void AssertRefused(string text, string rule)
    {
        IssueSearchRefusedException exception = Assert.Throws<IssueSearchRefusedException>(() =>
            IssueSearchText.Words(text: text, parameterName: PARAMETER)
        );

        Assert.Equal(expected: "text", actual: exception.Subject);
        Assert.Equal(expected: rule, actual: exception.Rule);
        Assert.Equal(expected: "text refused: " + rule, actual: exception.Message);
    }
}
