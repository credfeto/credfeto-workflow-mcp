using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Runs;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunLogSanitiserTests : TestBase
{
    private static RunLogLine Line(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        return RunLogSanitiser.Line(bytes: bytes, over: bytes.Length > RunLogSanitiser.MAX_LINE_BYTES);
    }

    private static string Repeat(string value, int count)
    {
        return string.Concat(Enumerable.Repeat(element: value, count: count));
    }

    [Fact]
    public void CheckThatSucceedsGivesItsResult()
    {
        string result = RunLogSanitiser.RedactOnCheckFailure(
            check: () => "checked",
            redacted: RunLogSanitiser.REDACTED_NAME
        );

        Assert.Equal(expected: "checked", actual: result);
    }

    [Fact]
    public void CheckThatTimesOutIsRedacted()
    {
        string result = RunLogSanitiser.RedactOnCheckFailure<string>(
            check: () => throw new RegexMatchTimeoutException(),
            redacted: RunLogSanitiser.REDACTED_NAME
        );

        Assert.Equal(expected: RunLogSanitiser.REDACTED_NAME, actual: result);
    }

    [Fact]
    public void CheckThatFailsIsRedacted()
    {
        RunLogLine redacted = new(Text: RunLogSanitiser.REDACTED_LINE, Redacted: true, Masked: false);

        RunLogLine result = RunLogSanitiser.RedactOnCheckFailure<RunLogLine>(
            check: () => throw new LeakCheckFailedException("the text could not be checked for leaks in time"),
            redacted: redacted
        );

        Assert.Equal(expected: redacted, actual: result);
    }

    [Fact]
    public void PlainLineIsKeptAsItIs()
    {
        RunLogLine line = Line("Build succeeded.");

        Assert.Equal(expected: new RunLogLine(Text: "Build succeeded.", Redacted: false, Masked: false), actual: line);
    }

    [Fact]
    public void LineOfExactlyTheLimitIsNotCut()
    {
        string text = new('x', RunLogSanitiser.MAX_LINE_BYTES);

        RunLogLine line = Line(text);

        Assert.Equal(expected: text, actual: line.Text);
    }

    [Fact]
    public void LongLineIsCutAtTheLimit()
    {
        RunLogLine line = Line(new string('x', RunLogSanitiser.MAX_LINE_BYTES + 1));

        Assert.Equal(
            expected: new string('x', RunLogSanitiser.MAX_LINE_BYTES) + RunLogSanitiser.CUT_SUFFIX,
            actual: line.Text
        );
        Assert.False(condition: line.Redacted, userMessage: "the line should not be redacted");
        Assert.False(condition: line.Masked, userMessage: "the line should not be masked");
    }

    [Fact]
    public void CutInsideACharacterMovesBackToItsStart()
    {
        // The two-byte character starts at the last kept byte, so cutting at the limit would split it
        string text = new string('x', RunLogSanitiser.MAX_LINE_BYTES - 1) + "é" + "tail";

        RunLogLine line = Line(text);

        Assert.Equal(
            expected: new string('x', RunLogSanitiser.MAX_LINE_BYTES - 1) + RunLogSanitiser.CUT_SUFFIX,
            actual: line.Text
        );
    }

    [Fact]
    public void CutAfterAWholeCharacterKeepsIt()
    {
        string text = new string('x', RunLogSanitiser.MAX_LINE_BYTES - 2) + "é" + "tail";

        RunLogLine line = Line(text);

        Assert.Equal(
            expected: new string('x', RunLogSanitiser.MAX_LINE_BYTES - 2) + "é" + RunLogSanitiser.CUT_SUFFIX,
            actual: line.Text
        );
    }

    [Theory]
    [InlineData("\u001b[31mred\u001b[0m text", "red text")]
    [InlineData("\u001b]0;title\u0007text", "text")]
    [InlineData("\u001b]8;;https://example.com\u001b\\link", "link")]
    [InlineData("\u001b]0;unterminated", "")]
    [InlineData("a\u001bMb", "ab")]
    public static void AnsiEscapesAreStripped(string text, string expected)
    {
        RunLogLine line = Line(text);

        Assert.Equal(expected: expected, actual: line.Text);
    }

    [Fact]
    public void ControlCharactersBecomeSpaces()
    {
        RunLogLine line = Line("a\tb\rc\u0000d" + (char)0x2028 + "e");

        Assert.Equal(expected: "a b c d e", actual: line.Text);
    }

    [Fact]
    public void InvalidUtf8KeepsAsciiAndShowsOtherBytesAsQuestionMarks()
    {
        RunLogLine line = RunLogSanitiser.Line(bytes: [0x61, 0xFF, 0xC3, 0x62], over: false);

        Assert.Equal(expected: "a??b", actual: line.Text);
    }

    [Fact]
    public void LineWithATokenIsRedacted()
    {
        RunLogLine line = Line("token: gh" + "p_a");

        Assert.Equal(
            expected: new RunLogLine(Text: RunLogSanitiser.REDACTED_LINE, Redacted: true, Masked: false),
            actual: line
        );
    }

    [Fact]
    public void TokenHiddenByAnEscapeIsStillRedacted()
    {
        RunLogLine line = Line("gh\u001b[0m" + "p_a");

        Assert.True(condition: line.Redacted, userMessage: "the line should be redacted");
        Assert.Equal(expected: RunLogSanitiser.REDACTED_LINE, actual: line.Text);
    }

    [Fact]
    public void HexRunOfFortyDigitsIsKept()
    {
        string sha = Repeat(value: "0123456789", count: 4);

        RunLogLine line = Line("commit " + sha);

        Assert.Equal(expected: "commit " + sha, actual: line.Text);
        Assert.False(condition: line.Masked, userMessage: "the line should not be masked");
    }

    [Fact]
    public void HexRunOfFortyOneDigitsIsMasked()
    {
        RunLogLine line = Line("value " + Repeat(value: "0123456789", count: 4) + "f end");

        Assert.Equal(expected: "value " + RunLogSanitiser.MASK + " end", actual: line.Text);
        Assert.True(condition: line.Masked, userMessage: "the line should be masked");
        Assert.False(condition: line.Redacted, userMessage: "the line should not be redacted");
    }

    [Fact]
    public void Base64RunWithMixedCaseAndDigitsIsMasked()
    {
        RunLogLine line = Line("blob " + Repeat(value: "Xyz789", count: 7) + "== end");

        Assert.Equal(expected: "blob " + RunLogSanitiser.MASK + " end", actual: line.Text);
        Assert.True(condition: line.Masked, userMessage: "the line should be masked");
    }

    [Fact]
    public void Base64RunOfFortyCharactersIsKept()
    {
        string run = Repeat(value: "Xyz789", count: 6) + "Xyz7";

        RunLogLine line = Line("blob " + run);

        Assert.Equal(expected: "blob " + run, actual: line.Text);
        Assert.False(condition: line.Masked, userMessage: "the line should not be masked");
    }

    [Theory]
    [InlineData("QQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQQ")]
    [InlineData("qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq")]
    [InlineData("QqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQqQq")]
    public static void LongRunWithoutAllThreeKindsIsKept(string run)
    {
        RunLogLine line = Line(run);

        Assert.Equal(expected: run, actual: line.Text);
        Assert.False(condition: line.Masked, userMessage: "the line should not be masked");
    }

    [Fact]
    public void RunnerPathIsKept()
    {
        const string path =
            "/home/runner/work/credfeto-workflow-mcp/credfeto-workflow-mcp/src/Credfeto.Workflow.GitHub/Runs/RunLogService.cs(12,3): error CS0103";

        RunLogLine line = Line(path);

        Assert.Equal(expected: path, actual: line.Text);
        Assert.False(condition: line.Masked, userMessage: "the line should not be masked");
    }

    [Fact]
    public void GitHubMaskIsKept()
    {
        RunLogLine line = Line("value is ***");

        Assert.Equal(expected: new RunLogLine(Text: "value is ***", Redacted: false, Masked: false), actual: line);
    }

    [Fact]
    public void EmptyLineIsKeptEmpty()
    {
        RunLogLine line = Line(string.Empty);

        Assert.Equal(expected: string.Empty, actual: line.Text);
    }

    [Fact]
    public void NameIsMadePrintable()
    {
        Assert.Equal(expected: "build and test", actual: RunLogSanitiser.Name("build\nand\ttest"));
    }

    [Fact]
    public void MissingNameIsShownAsNone()
    {
        Assert.Equal(expected: "-", actual: RunLogSanitiser.Name(null));
    }

    [Fact]
    public void NameWithATokenIsRedacted()
    {
        Assert.Equal(expected: RunLogSanitiser.REDACTED_NAME, actual: RunLogSanitiser.Name("deploy gh" + "p_a"));
    }
}
