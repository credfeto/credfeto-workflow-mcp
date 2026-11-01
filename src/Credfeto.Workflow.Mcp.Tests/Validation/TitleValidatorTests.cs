using System;
using System.Linq;
using Credfeto.Workflow.Mcp.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Validation;

public sealed class TitleValidatorTests : TestBase
{
    private const string LISTED_OWNER = "credfeto";
    private const string OTHER_OWNER = "octocat";
    private const string PREFIX = "fix: ";

    [Fact]
    public static void MessagesAreFixedText()
    {
        Assert.Equal(
            expected: "invalid value for --title: it must not be blank or longer than 256 characters",
            actual: TitleValidator.INVALID_TITLE_MESSAGE
        );
        Assert.Equal(
            expected: "invalid value for --title: it must be \"prefix: text\" or \"prefix(name): text\" with a lower-case prefix, for example \"oneshot: ...\" or \"tool(cfwf): ...\"",
            actual: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE
        );
        Assert.Equal(
            expected: "invalid value for --title: it must be a Conventional Commits title, <type>[(<scope>)][!]: <description>",
            actual: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE
        );
    }

    [Fact]
    public static void RegexesHaveATimeout()
    {
        TimeSpan expected = TimeSpan.FromSeconds(1);

        Assert.Equal(expected: expected, actual: TitleValidator.IssueTitle().MatchTimeout);
        Assert.Equal(expected: expected, actual: TitleValidator.PullRequestTitle().MatchTimeout);
    }

    [Theory]
    [InlineData("fix: something")]
    [InlineData("Anything at all")]
    [InlineData(" padded ")]
    [InlineData("\U00003000x")]
    [InlineData("\U0000200Bx")]
    public static void TitleAcceptsVisibleText(string title)
    {
        Assert.True(condition: TitleValidator.IsValidTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\U00003000")]
    [InlineData("\U000000A0\U00002007\U0000202F\U00002800")]
    [InlineData("\U0000200B\U0000200C\U0000200D\U0000FEFF")]
    [InlineData("\U00003164\U0000FFA0\U0000115F\U00001160")]
    [InlineData("\U000E0020\U000E007F")]
    [InlineData("\U0001D173 \U0001BCA0")]
    public static void TitleRejectsBlankOrInvisibleText(string title)
    {
        Assert.False(condition: TitleValidator.IsValidTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("fix: a\nb")]
    [InlineData("fix: a\n")]
    [InlineData("fix: a\r")]
    [InlineData("fix:\tx")]
    [InlineData("\0fix: x")]
    [InlineData("fix: a\u007F")]
    [InlineData("fix: a\u0085")]
    public static void TitleRejectsControlCharacters(string title)
    {
        Assert.False(condition: TitleValidator.IsValidTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\U000000E9")]
    [InlineData("\U0001F600")]
    public static void TitleAcceptsExactlyTheMaximumLength(string character)
    {
        string title = PREFIX + Repeat(value: character, count: TitleValidator.MAX_TITLE_LENGTH - PREFIX.Length);

        Assert.True(condition: TitleValidator.IsValidTitle(title), userMessage: character);
        Assert.True(condition: TitleValidator.IsValidPullRequestTitle(title), userMessage: character);
        Assert.True(
            condition: TitleValidator.IsValidIssueTitle(owner: LISTED_OWNER, title: title),
            userMessage: character
        );
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\U000000E9")]
    [InlineData("\U0001F600")]
    public static void TitleRejectsOneOverTheMaximumLength(string character)
    {
        string title = PREFIX + Repeat(value: character, count: TitleValidator.MAX_TITLE_LENGTH - PREFIX.Length + 1);

        Assert.False(condition: TitleValidator.IsValidTitle(title), userMessage: character);
        Assert.False(condition: TitleValidator.IsValidPullRequestTitle(title), userMessage: character);
        Assert.False(
            condition: TitleValidator.IsValidIssueTitle(owner: OTHER_OWNER, title: title),
            userMessage: character
        );
    }

    [Fact]
    public static void TitleLengthCountsInvisibleCharacters()
    {
        string title =
            PREFIX
            + Repeat(value: "a", count: TitleValidator.MAX_TITLE_LENGTH - PREFIX.Length - 1)
            + "\U0000200B\U0000200B";

        Assert.False(condition: TitleValidator.IsValidTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("oneshot: do something")]
    [InlineData("tool(cfwf): add a check")]
    [InlineData("fix(a b): x")]
    [InlineData("fix(a): x")]
    [InlineData("tool-1: x")]
    [InlineData("a-b-c: x")]
    [InlineData("x1: y")]
    [InlineData("fix:  two spaces")]
    [InlineData("fix: text with (brackets): and colons")]
    [InlineData("fix: \U00003000x")]
    public static void IssueTitleAcceptsPrefixShapeForListedOwner(string title)
    {
        Assert.True(condition: TitleValidator.IsValidIssueTitle(owner: LISTED_OWNER, title: title), userMessage: title);
    }

    [Theory]
    [InlineData("Tool: x")]
    [InlineData("1tool: x")]
    [InlineData("tool-: x")]
    [InlineData("tool--x: x")]
    [InlineData("-tool: x")]
    [InlineData("fix(): x")]
    [InlineData("fix( a): x")]
    [InlineData("fix(a ): x")]
    [InlineData("fix(a:b): x")]
    [InlineData("fix((a)): x")]
    [InlineData("fix(a)b: x")]
    [InlineData("fix!: x")]
    [InlineData("fix:x")]
    [InlineData("fix x")]
    [InlineData("fix:")]
    [InlineData("fix :x")]
    [InlineData("fix:   ")]
    [InlineData("fix: \U00003000")]
    [InlineData("fix: \U0000200B\U000000A0")]
    [InlineData(" fix: x")]
    public static void IssueTitleRejectsOtherShapesForListedOwner(string title)
    {
        Assert.False(
            condition: TitleValidator.IsValidIssueTitle(owner: LISTED_OWNER, title: title),
            userMessage: title
        );
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("CredFeto")]
    [InlineData("CREDFETO")]
    [InlineData("funfair-tech")]
    [InlineData("FunFair-Tech")]
    public static void IssueTitleShapeAppliesToListedOwnersInAnyCase(string owner)
    {
        Assert.False(condition: TitleValidator.IsValidIssueTitle(owner: owner, title: "Free text"), userMessage: owner);
        Assert.True(condition: TitleValidator.IsValidIssueTitle(owner: owner, title: "tool: x"), userMessage: owner);
    }

    [Theory]
    [InlineData("octocat")]
    [InlineData("credfeto2")]
    [InlineData("funfair")]
    [InlineData("")]
    public static void IssueTitleShapeDoesNotApplyToOtherOwners(string owner)
    {
        Assert.True(condition: TitleValidator.IsValidIssueTitle(owner: owner, title: "Free text"), userMessage: owner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\U00003000")]
    [InlineData("\U0000200B")]
    [InlineData("free\ntext")]
    public static void IssueTitleCommonRulesApplyToOtherOwners(string title)
    {
        Assert.False(condition: TitleValidator.IsValidIssueTitle(owner: OTHER_OWNER, title: title), userMessage: title);
    }

    [Theory]
    [InlineData("feat: x")]
    [InlineData("feat(api): x")]
    [InlineData("feat!: x")]
    [InlineData("feat(api)!: x")]
    [InlineData("feat( a ): x")]
    [InlineData("feat(a:b): x")]
    [InlineData("fix: x  y")]
    [InlineData("chore(deps): bump something from 1 to 2")]
    public static void PullRequestTitleAcceptsConventionalCommits(string title)
    {
        Assert.True(condition: TitleValidator.IsValidPullRequestTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("Feat: x")]
    [InlineData("feat1: x")]
    [InlineData("feat-x: x")]
    [InlineData("feat(): x")]
    [InlineData("feat((a)): x")]
    [InlineData("feat(a)b: x")]
    [InlineData("feat!(a): x")]
    [InlineData("feat!!: x")]
    [InlineData("feat:  x")]
    [InlineData("feat:x")]
    [InlineData("feat: ")]
    [InlineData("feat")]
    [InlineData(" feat: x")]
    [InlineData("feat: \U00003000")]
    [InlineData("feat: \U0000200B")]
    [InlineData("Free text")]
    [InlineData("")]
    [InlineData("feat: a\n")]
    public static void PullRequestTitleRejectsOtherShapes(string title)
    {
        Assert.False(condition: TitleValidator.IsValidPullRequestTitle(title), userMessage: title);
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("good first issue")]
    [InlineData("\U0001F600")]
    public static void LabelAcceptsValidValues(string label)
    {
        Assert.True(condition: TitleValidator.IsValidLabel(label), userMessage: label);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\U0000200B")]
    [InlineData("a,b")]
    [InlineData(",")]
    [InlineData("a\nb")]
    [InlineData("a\u0085")]
    public static void LabelRejectsInvalidValues(string label)
    {
        Assert.False(condition: TitleValidator.IsValidLabel(label), userMessage: label);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\U0001F600")]
    public static void LabelLengthIsLimited(string character)
    {
        Assert.True(
            condition: TitleValidator.IsValidLabel(Repeat(value: character, count: TitleValidator.MAX_LABEL_LENGTH)),
            userMessage: character
        );
        Assert.False(
            condition: TitleValidator.IsValidLabel(
                Repeat(value: character, count: TitleValidator.MAX_LABEL_LENGTH + 1)
            ),
            userMessage: character
        );
    }

    [Theory]
    [InlineData("abc", 3, true)]
    [InlineData("abcd", 3, false)]
    [InlineData("\U0001F600\U0001F600\U0001F600", 3, true)]
    [InlineData(" ", 3, false)]
    public static void TextHonoursTheGivenMaximumLength(string value, int maxLength, bool expected)
    {
        Assert.Equal(expected: expected, actual: TitleValidator.IsValidText(value: value, maxLength: maxLength));
    }

    [Theory]
    [InlineData("x", true)]
    [InlineData(" x ", true)]
    [InlineData("", false)]
    [InlineData(" \t", false)]
    [InlineData("\U00003000\U0000200B", false)]
    public static void HasVisibleTextIgnoresWhitespaceAndInvisibleCharacters(string value, bool expected)
    {
        Assert.Equal(expected: expected, actual: TitleValidator.HasVisibleText(value));
    }

    private static string Repeat(string value, int count)
    {
        return string.Concat(Enumerable.Repeat(element: value, count: count));
    }
}
