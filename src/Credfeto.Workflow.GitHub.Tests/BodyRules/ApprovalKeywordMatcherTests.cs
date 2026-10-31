using Credfeto.Workflow.GitHub.BodyRules;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.BodyRules;

public sealed class ApprovalKeywordMatcherTests : TestBase
{
    // Built from fragments so this file does not itself read as a plan sign-off when quoted in a review
    private const string PLAN_KEYWORD = "appro" + "ved";
    private const string SHORT_KEYWORD = "lg" + "tm";

    [Theory]
    [InlineData(PLAN_KEYWORD)]
    [InlineData("APPRO" + "VED")]
    [InlineData("Appro" + "Ved")]
    [InlineData(SHORT_KEYWORD)]
    [InlineData("LG" + "TM")]
    [InlineData("Lg" + "Tm")]
    [InlineData("Looks good, " + PLAN_KEYWORD + ".")]
    [InlineData("(" + SHORT_KEYWORD + ")")]
    [InlineData("_" + PLAN_KEYWORD + "_")]
    [InlineData("__" + SHORT_KEYWORD + "__")]
    [InlineData("the plan is _" + PLAN_KEYWORD + "_ now")]
    [InlineData("    > " + PLAN_KEYWORD)]
    [InlineData("\t> " + SHORT_KEYWORD)]
    [InlineData(PLAN_KEYWORD + "\r")]
    public static void KeywordOnFirstLineIsReported(string body)
    {
        ApprovalKeywordMatch? match = ApprovalKeywordMatcher.FindFirstKeywordLine(body);

        Assert.Equal(expected: new ApprovalKeywordMatch(1), actual: match);
        Assert.True(ApprovalKeywordMatcher.HasKeyword(body), userMessage: body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nothing to see here")]
    [InlineData("plan_" + PLAN_KEYWORD)]
    [InlineData(PLAN_KEYWORD + "_plan")]
    [InlineData("un" + PLAN_KEYWORD)]
    [InlineData(SHORT_KEYWORD + "2")]
    [InlineData("2" + SHORT_KEYWORD)]
    [InlineData(PLAN_KEYWORD + "s")]
    [InlineData("_" + PLAN_KEYWORD + "_x")]
    [InlineData("> " + PLAN_KEYWORD)]
    [InlineData(" > " + PLAN_KEYWORD)]
    [InlineData("  > " + SHORT_KEYWORD)]
    [InlineData("   > " + SHORT_KEYWORD)]
    [InlineData(">" + PLAN_KEYWORD)]
    [InlineData("first line\n> " + PLAN_KEYWORD + "\nlast line")]
    public static void BodyWithoutUnquotedKeywordPasses(string body)
    {
        Assert.Null(ApprovalKeywordMatcher.FindFirstKeywordLine(body));
        Assert.False(ApprovalKeywordMatcher.HasKeyword(body), userMessage: body);
    }

    [Theory]
    [InlineData("first\nsecond\n" + PLAN_KEYWORD, 3)]
    [InlineData("> " + PLAN_KEYWORD + "\n" + SHORT_KEYWORD, 2)]
    [InlineData("one\r\ntwo\r\nthree " + SHORT_KEYWORD + "\r\n" + PLAN_KEYWORD, 3)]
    [InlineData("\n\n\n\n" + SHORT_KEYWORD, 5)]
    public static void FirstMatchingLineNumberIsReported(string body, int expectedLine)
    {
        ApprovalKeywordMatch? match = ApprovalKeywordMatcher.FindFirstKeywordLine(body);

        Assert.Equal(expected: new ApprovalKeywordMatch(expectedLine), actual: match);
        Assert.True(ApprovalKeywordMatcher.HasKeyword(body), userMessage: body);
    }
}
