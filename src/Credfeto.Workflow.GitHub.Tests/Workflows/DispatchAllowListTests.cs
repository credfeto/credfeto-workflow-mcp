using System;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Workflows;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Workflows;

public sealed class DispatchAllowListTests : TestBase
{
    private const string DEFAULT_BRANCH = "main";

    private static readonly string ByteOrderMark = new(c: (char)0xFEFF, count: 1);

    [Fact]
    public void AWorkflowAloneIsPermittedOnTheDefaultBranchOnly()
    {
        DispatchAllowEntry entry = Single(DispatchAllowList.Parse("deploy.yml\n"), workflow: "deploy.yml");

        Assert.True(
            entry.Permits(gitRef: DEFAULT_BRANCH, defaultBranch: DEFAULT_BRANCH),
            userMessage: "default branch"
        );
        Assert.False(entry.Permits(gitRef: "release/1", defaultBranch: DEFAULT_BRANCH), userMessage: "other ref");
    }

    [Fact]
    public void ListedRefsArePermittedExactly()
    {
        DispatchAllowEntry entry = Single(
            DispatchAllowList.Parse("deploy.yml release/1 hotfix\r\n"),
            workflow: "deploy.yml"
        );

        Assert.True(entry.Permits(gitRef: "release/1", defaultBranch: DEFAULT_BRANCH), userMessage: "release/1");
        Assert.True(entry.Permits(gitRef: "hotfix", defaultBranch: DEFAULT_BRANCH), userMessage: "hotfix");
        Assert.False(entry.Permits(gitRef: "Release/1", defaultBranch: DEFAULT_BRANCH), userMessage: "case differs");
        Assert.False(entry.Permits(gitRef: "release/12", defaultBranch: DEFAULT_BRANCH), userMessage: "prefix");
    }

    [Fact]
    public void AStarPermitsAnyRef()
    {
        DispatchAllowEntry entry = Single(DispatchAllowList.Parse("deploy.yml *"), workflow: "deploy.yml");

        Assert.True(entry.Permits(gitRef: "anything", defaultBranch: DEFAULT_BRANCH), userMessage: "any ref");
    }

    [Fact]
    public void LinesForTheSameWorkflowAreCombined()
    {
        DispatchAllowEntry entry = Single(
            DispatchAllowList.Parse("deploy.yml one\ndeploy.yml two\n"),
            workflow: "deploy.yml"
        );

        Assert.True(entry.Permits(gitRef: "one", defaultBranch: DEFAULT_BRANCH), userMessage: "one");
        Assert.True(entry.Permits(gitRef: "two", defaultBranch: DEFAULT_BRANCH), userMessage: "two");
    }

    [Fact]
    public void CommentsBlankLinesAndAByteOrderMarkAreIgnored()
    {
        DispatchAllowEntry entry = Single(
            DispatchAllowList.Parse(ByteOrderMark + "# allowed workflows\n\n   \ndeploy.yml one # but not two\n"),
            workflow: "deploy.yml"
        );

        Assert.True(entry.Permits(gitRef: "one", defaultBranch: DEFAULT_BRANCH), userMessage: "one");
        Assert.False(entry.Permits(gitRef: "two", defaultBranch: DEFAULT_BRANCH), userMessage: "commented out");
    }

    [Theory]
    [InlineData("deploy.yml\tone")]
    [InlineData(".github/workflows/deploy.yml")]
    [InlineData("deploy.txt")]
    [InlineData("-deploy.yml")]
    [InlineData("deploy.yml -one")]
    [InlineData("deploy.yml HEAD")]
    [InlineData("deploy.yml one..two")]
    [InlineData("deploy.yml good bad~ref")]
    public static void AMalformedLineIsIgnored(string line)
    {
        IReadOnlyDictionary<string, DispatchAllowEntry> entries = DispatchAllowList.Parse(line);

        Assert.Empty(entries);
    }

    [Fact]
    public void AMalformedLineDoesNotAffectOtherLines()
    {
        IReadOnlyDictionary<string, DispatchAllowEntry> entries = DispatchAllowList.Parse(
            "deploy.yml bad~ref\nbuild.yaml\n"
        );

        Assert.Equal(expected: ["build.yaml"], actual: entries.Keys, comparer: StringComparer.Ordinal);
    }

    [Fact]
    public void WorkflowNamesAreCaseSensitive()
    {
        IReadOnlyDictionary<string, DispatchAllowEntry> entries = DispatchAllowList.Parse("Deploy.yml");

        Assert.False(entries.ContainsKey("deploy.yml"), userMessage: "names are matched exactly");
    }

    private static DispatchAllowEntry Single(IReadOnlyDictionary<string, DispatchAllowEntry> entries, string workflow)
    {
        Assert.Equal(expected: [workflow], actual: entries.Keys, comparer: StringComparer.Ordinal);

        return entries[workflow];
    }
}
