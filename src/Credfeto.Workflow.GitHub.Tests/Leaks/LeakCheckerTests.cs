using System;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Leaks;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection
public sealed class LeakCheckerTests : TestBase
{
    private const string SECRET = "gh" + "p_abcdef";

    private readonly ILeakChecker _leakChecker = new LeakChecker();

    [Fact]
    public void CleanBodyPasses()
    {
        Assert.Null(this._leakChecker.CheckBody("line one\nline two\twith a tab"));
    }

    [Fact]
    public void TokenIsReportedWithItsLineNumber()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("line one\ntoken " + SECRET + "\nline three");

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2),
            actual: refusal
        );
    }

    [Fact]
    public void EarlierRuleIsReportedBeforeEarlierLine()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("see /home/user\n" + SECRET);

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2),
            actual: refusal
        );
    }

    [Fact]
    public void HostPathIsReported()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("ok\nok\nsee /tmp/x");

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.HOST_PATH, LineNumber: 3),
            actual: refusal
        );
    }

    [Fact]
    public void CarriageReturnIsReportedAsControlCharacter()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("a\r\nb");

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.CONTROL_CHARACTER, LineNumber: 1),
            actual: refusal
        );
    }

    [Fact]
    public void InvalidUtf8IsReported()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("ok\nbad " + (char)0xD800);

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.INVALID_UTF8, LineNumber: 2),
            actual: refusal
        );
    }

    [Fact]
    public void LongBodyIsReportedWithoutLineNumber()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody(new('a', count: LeakPatterns.MAXIMUM_BODY_LENGTH + 1));

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOO_LONG, LineNumber: null),
            actual: refusal
        );
    }

    [Fact]
    public void LongValueIsNotLimited()
    {
        Assert.Null(this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: new('a', count: 70000)));
    }

    [Theory]
    [InlineData(LeakSubject.TITLE)]
    [InlineData(LeakSubject.LABEL)]
    [InlineData(LeakSubject.TEXT)]
    public void ValueRefusalNamesItsSubject(LeakSubject subject)
    {
        LeakRefusal? refusal = this._leakChecker.CheckValue(subject: subject, value: "fix " + SECRET);

        Assert.Equal(expected: new LeakRefusal(Subject: subject, Rule: LeakRule.TOKEN, LineNumber: 1), actual: refusal);
    }

    [Fact]
    public void CleanValuePasses()
    {
        Assert.Null(this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: "MONKEY=banana"));
    }

    [Fact]
    public void RefusalDoesNotHoldTheMatchedText()
    {
        LeakRefusal? refusal = this._leakChecker.CheckBody("x " + SECRET);

        Assert.NotNull(refusal);
        Assert.DoesNotContain(
            expectedSubstring: SECRET,
            actualString: refusal.ToString(),
            comparisonType: StringComparison.Ordinal
        );
    }
}
