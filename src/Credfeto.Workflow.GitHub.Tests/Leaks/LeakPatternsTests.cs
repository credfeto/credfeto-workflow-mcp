using System;
using System.Linq;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Leaks;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection
public sealed partial class LeakPatternsTests : TestBase
{
    private const string ALPHANUMERIC_36 = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string ALPHANUMERIC_35 = "abcdefghijklmnopqrstuvwxyz012345678";
    private const string ALPHANUMERIC_48 = ALPHANUMERIC_36 + "ABCDEFGHIJKL";
    private const string AWS_KEY_BODY = "ABCDEFGHIJKLMNOP";
    private const string BEARER_VALUE = "abcdefghijklmnopqrs1";
    private const string CATASTROPHIC_INPUT = "abcdefghijklmnopqrstuvwxyzabcdefghijklmn!";

    [Theory]
    [InlineData("gh" + "p_a")]
    [InlineData("gh" + "o_a")]
    [InlineData("gh" + "r_a")]
    [InlineData("gh" + "s_a")]
    [InlineData("gh" + "u_a")]
    [InlineData("token: gh" + "p_a")]
    [InlineData("xo" + "xb-1")]
    [InlineData("xo" + "xp-1")]
    [InlineData("gl" + "pat-x")]
    [InlineData("sk-" + "ant-x")]
    [InlineData("sk-" + "proj-x")]
    [InlineData("np" + "m_" + ALPHANUMERIC_36)]
    [InlineData("AI" + "za" + ALPHANUMERIC_35)]
    [InlineData("sk" + "-" + ALPHANUMERIC_48)]
    [InlineData("https://x-access-" + "token:pw@github.com/a/b")]
    [InlineData("github" + "_pat_a")]
    [InlineData("xgithub" + "_pat_a")]
    [InlineData("AK" + "IA" + AWS_KEY_BODY)]
    [InlineData("AS" + "IA" + AWS_KEY_BODY)]
    [InlineData("-----BEGIN " + "PRIVATE KEY-----")]
    [InlineData("-----BEGIN " + "RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN " + "OPENSSH ENCRYPTED PRIVATE KEY-----")]
    [InlineData("https://discord.com/api/" + "webhooks/123/abc")]
    [InlineData("https://Discordapp.com/api/" + "webhooks/1/a")]
    [InlineData("https://user:" + "pw@example.com")]
    [InlineData("https://:" + "pw@example.com")]
    [InlineData("Authorization: " + "x")]
    [InlineData("authorization=" + "x")]
    [InlineData("AUTHORIZATION : " + "x")]
    [InlineData("\"Authorization\": " + "\"x\"")]
    [InlineData("\\\"Authorization\\\": " + "x")]
    [InlineData("Bearer " + BEARER_VALUE)]
    [InlineData("bearer " + BEARER_VALUE)]
    [InlineData("(BEARER " + BEARER_VALUE + ")")]
    [InlineData("TOKEN=" + "x")]
    [InlineData("MY_TOKEN=" + "x")]
    [InlineData("export API_KEY=" + "x")]
    [InlineData("SECRET='" + "x'")]
    [InlineData("PASSWORD=\"" + "x\"")]
    public static void TokenIsRefused(string line)
    {
        Assert.True(LeakPatterns.HasToken(line), userMessage: line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("ghp_")]
    [InlineData("aghp" + "_a")]
    [InlineData("np" + "m_abc")]
    [InlineData("AI" + "zaShort")]
    [InlineData("sk-abc")]
    [InlineData("AK" + "IA" + "ABCDEFGHIJKLMNO")]
    [InlineData("-----BEGIN PUBLIC KEY-----")]
    [InlineData("https://discord.com/api/webhooks/")]
    [InlineData("https://example.com/path")]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com:8080/path")]
    [InlineData("Authorization")]
    [InlineData("The Authorization header is required")]
    [InlineData("a Bearer token")]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxy")]
    [InlineData("Bearer " + "abcdefghijklmnopq1")]
    [InlineData("MONKEY=banana")]
    [InlineData("TOKEN=$X")]
    [InlineData("TOKEN=\"$X\"")]
    [InlineData("TOKEN=\"\"")]
    [InlineData("TOKEN=")]
    [InlineData("token=x")]
    [InlineData("TOKEN: x")]
    [InlineData("TOKENS=x")]
    public static void TokenFreeLinePasses(string line)
    {
        Assert.False(LeakPatterns.HasToken(line), userMessage: line);
    }

    [Theory]
    [InlineData("/home/user")]
    [InlineData("see /tmp/x")]
    [InlineData("(/root/.ssh)")]
    [InlineData("/run/user/1000/bus")]
    [InlineData("/Users/bob/")]
    [InlineData("file:///home/x")]
    [InlineData("/var/tmp/x")]
    [InlineData("file:///var/tmp/x")]
    [InlineData("in /workspace/tmp/x")]
    [InlineData("C" + ":\\Users\\bob")]
    [InlineData("c:/users/bob")]
    [InlineData("(D:\\\\USERS\\x)")]
    public static void HostPathIsRefused(string line)
    {
        Assert.True(LeakPatterns.HasHostPath(line), userMessage: line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("./tmp/x")]
    [InlineData("~/tmp/x")]
    [InlineData("../home/x")]
    [InlineData("home/user")]
    [InlineData("src/home/x")]
    [InlineData("https://example.com/home/page")]
    [InlineData("/homes/x")]
    [InlineData("/home")]
    [InlineData("D" + ":\\Projects\\x")]
    [InlineData("abc:\\Users\\x")]
    public static void HostPathFreeLinePasses(string line)
    {
        Assert.False(LeakPatterns.HasHostPath(line), userMessage: line);
    }

    [Theory]
    [InlineData("a\u0000b")]
    [InlineData("a\u0001b")]
    [InlineData("a\u0008b")]
    [InlineData("a\u000Bb")]
    [InlineData("a\rb")]
    [InlineData("a\u001Bb")]
    [InlineData("a\u007Fb")]
    [InlineData("a\u0085b")]
    [InlineData("a\u009Fb")]
    public static void ControlCharacterIsRefused(string line)
    {
        Assert.True(LeakPatterns.HasControlCharacter(line), userMessage: "control character should be refused");
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("a\tb")]
    [InlineData("a\u00A0b")]
    public static void ControlCharacterFreeLinePasses(string line)
    {
        Assert.False(LeakPatterns.HasControlCharacter(line), userMessage: line);
    }

    // Lone surrogates are built at run time: a test data attribute would have to serialise them
    [Fact]
    public static void LoneHighSurrogateIsRefused()
    {
        Assert.True(
            LeakPatterns.HasInvalidUtf8("a" + (char)0xD800 + "b"),
            userMessage: "invalid UTF-8 should be refused"
        );
    }

    [Fact]
    public static void LoneHighSurrogateAtEndIsRefused()
    {
        Assert.True(LeakPatterns.HasInvalidUtf8("a" + (char)0xDBFF), userMessage: "invalid UTF-8 should be refused");
    }

    [Fact]
    public static void LoneLowSurrogateIsRefused()
    {
        Assert.True(
            LeakPatterns.HasInvalidUtf8("a" + (char)0xDC00 + "b"),
            userMessage: "invalid UTF-8 should be refused"
        );
    }

    [Fact]
    public static void ReplacementCharacterIsRefused()
    {
        Assert.True(LeakPatterns.HasInvalidUtf8("a\uFFFDb"), userMessage: "invalid UTF-8 should be refused");
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("caf\u00E9 \uD83D\uDE00")]
    public static void ValidTextPassesUtf8Check(string line)
    {
        Assert.False(LeakPatterns.HasInvalidUtf8(line), userMessage: line);
    }

    [Fact]
    public static void BodyAtLimitIsNotTooLong()
    {
        Assert.False(
            LeakPatterns.IsTooLong(new('a', count: LeakPatterns.MAXIMUM_BODY_LENGTH)),
            userMessage: "body should not be too long"
        );
    }

    [Fact]
    public static void BodyOverLimitIsTooLong()
    {
        Assert.True(
            LeakPatterns.IsTooLong(new('a', count: LeakPatterns.MAXIMUM_BODY_LENGTH + 1)),
            userMessage: "body should be too long"
        );
    }

    [Fact]
    public static void BodyOfAstralCharactersAtLimitIsNotTooLong()
    {
        string body = string.Concat(
            Enumerable.Repeat(element: "\uD83D\uDE00", count: LeakPatterns.MAXIMUM_BODY_LENGTH)
        );

        Assert.False(LeakPatterns.IsTooLong(body), userMessage: "body should not be too long");
    }

    [Fact]
    public static void BodyOfAstralCharactersOverLimitIsTooLong()
    {
        string body = string.Concat(
            Enumerable.Repeat(element: "\uD83D\uDE00", count: LeakPatterns.MAXIMUM_BODY_LENGTH + 1)
        );

        Assert.True(LeakPatterns.IsTooLong(body), userMessage: "body should be too long");
    }

    [Fact]
    public static void MatchingPatternIsReported()
    {
        Assert.True(LeakPatterns.IsMatch(pattern: Catastrophic(), line: "abc"), userMessage: "pattern should match");
    }

    [Fact]
    public static void TimeoutIsReportedWithoutTheText()
    {
        LeakCheckFailedException exception = Assert.Throws<LeakCheckFailedException>(() =>
            LeakPatterns.IsMatch(pattern: Catastrophic(), line: CATASTROPHIC_INPUT)
        );

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            expectedSubstring: CATASTROPHIC_INPUT,
            actualString: exception.ToString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public static void EveryMatchIsReported()
    {
        Assert.Equal(
            expected: ["ab", "cd"],
            actual: LeakPatterns.Matches(pattern: Word(), line: "ab cd").Select(static m => m.Value),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public static void MatchesTimeoutIsReportedWithoutTheText()
    {
        LeakCheckFailedException exception = Assert.Throws<LeakCheckFailedException>(() =>
            LeakPatterns.Matches(pattern: Catastrophic(), line: CATASTROPHIC_INPUT)
        );

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            expectedSubstring: CATASTROPHIC_INPUT,
            actualString: exception.ToString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [GeneratedRegex(pattern: @"\w+", options: RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Word();

    [GeneratedRegex(
        pattern: @"^(\w+\s?)*$",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1
    )]
    private static partial Regex Catastrophic();
}
