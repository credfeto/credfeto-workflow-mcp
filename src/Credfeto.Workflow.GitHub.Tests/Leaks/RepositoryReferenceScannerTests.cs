using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Leaks;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

public sealed class RepositoryReferenceScannerTests : TestBase
{
    private static readonly FrozenSet<string> NoPrivateOwners = FrozenSet.Create<string>(
        equalityComparer: StringComparer.OrdinalIgnoreCase
    );

    private static readonly FrozenSet<string> PrivateOwners = FrozenSet.Create(
        equalityComparer: StringComparer.OrdinalIgnoreCase,
        "secretorg"
    );

    [Theory]
    [InlineData("see https://github.com/credfeto/repo for details", "credfeto", "repo")]
    [InlineData("https://www.github.com/credfeto/repo/pull/1", "credfeto", "repo")]
    [InlineData("git clone git@github.com:credfeto/repo.git", "credfeto", "repo")]
    [InlineData("https://api.github.com/repos/credfeto/repo/issues", "credfeto", "repo")]
    [InlineData("https://raw.githubusercontent.com/credfeto/repo/main/README.md", "credfeto", "repo")]
    [InlineData("https://codeload.github.com/credfeto/repo/zip/main", "credfeto", "repo")]
    [InlineData("HTTPS://GITHUB.COM/Credfeto/Repo", "Credfeto", "Repo")]
    [InlineData("github.com/credfeto/repo", "credfeto", "repo")]
    [InlineData("and so on ...github.com/credfeto/repo", "credfeto", "repo")]
    [InlineData("https://github.com/credfeto/repo.git", "credfeto", "repo")]
    [InlineData("https://github.com/credfeto/repo.wiki", "credfeto", "repo")]
    [InlineData("https://github.com/credfeto/repo.wiki.git", "credfeto", "repo")]
    [InlineData("See https://github.com/credfeto/repo.", "credfeto", "repo")]
    [InlineData("https://github.com/credfeto/repo.GIT", "credfeto", "repo")]
    [InlineData("https://github.com/credfeto/my_repo.name", "credfeto", "my_repo.name")]
    [InlineData("Fixes credfeto/repo#12", "credfeto", "repo")]
    [InlineData("in credfeto/repo@abcdef1", "credfeto", "repo")]
    [InlineData("in credfeto/repo@0123456789abcdef0123456789abcdef01234567.", "credfeto", "repo")]
    [InlineData("_credfeto/repo#1_", "credfeto", "repo")]
    [InlineData("(credfeto/repo#1)", "credfeto", "repo")]
    [InlineData("host:credfeto/repo#1", "credfeto", "repo")]
    public static void LinkOrCrossReferenceIsFound(string text, string owner, string name)
    {
        IReadOnlyList<RepositoryReference> references = RepositoryReferenceScanner.Scan(
            text: text,
            privateOwners: NoPrivateOwners
        );

        Assert.Equal(expected: [new(LineNumber: 1, Owner: owner, Name: name)], actual: references);
    }

    [Theory]
    [InlineData("https://github.com/orgs/credfeto/projects/1")]
    [InlineData("https://github.com/settings/tokens")]
    [InlineData("https://github.com/topics/dotnet")]
    [InlineData("https://github.com/Marketplace/actions")]
    [InlineData("https://docs.github.com/en/rest/repos")]
    [InlineData("https://gist.github.com/credfeto/abc123")]
    [InlineData("https://notgithub.com/credfeto/repo")]
    [InlineData("https://github.com/credfeto/.git")]
    [InlineData("uses: actions/checkout@v4")]
    [InlineData("http://localhost:8080/path#1")]
    [InlineData("https://example.com/a/b#1")]
    [InlineData("see credfeto/repo for details")]
    [InlineData("path/secretorg/hidden")]
    [InlineData("credfeto/repo#abc")]
    [InlineData("credfeto/repo@abcdef")]
    public static void NonReferenceIsIgnored(string text)
    {
        Assert.Empty(RepositoryReferenceScanner.Scan(text: text, privateOwners: PrivateOwners));
    }

    [Theory]
    [InlineData("secretorg/hidden", "secretorg", "hidden")]
    [InlineData("see SecretOrg/Hidden.", "SecretOrg", "Hidden")]
    [InlineData("(secretorg/hidden.git)", "secretorg", "hidden")]
    public static void BareReferenceToPrivateOwnerIsFound(string text, string owner, string name)
    {
        IReadOnlyList<RepositoryReference> references = RepositoryReferenceScanner.Scan(
            text: text,
            privateOwners: PrivateOwners
        );

        Assert.Equal(expected: [new(LineNumber: 1, Owner: owner, Name: name)], actual: references);
    }

    [Fact]
    public static void BareReferenceIsIgnoredWithoutPrivateOwners()
    {
        Assert.Empty(RepositoryReferenceScanner.Scan(text: "secretorg/hidden", privateOwners: NoPrivateOwners));
    }

    [Fact]
    public static void ReferencesAreNumberedByLineAndOrderedByPosition()
    {
        IReadOnlyList<RepositoryReference> references = RepositoryReferenceScanner.Scan(
            text: "first line\nhttps://github.com/credfeto/a and secretorg/b\n\nother/c#3 then credfeto/d#4",
            privateOwners: PrivateOwners
        );

        Assert.Equal(
            expected:
            [
                new(LineNumber: 2, Owner: "credfeto", Name: "a"),
                new(LineNumber: 2, Owner: "secretorg", Name: "b"),
                new(LineNumber: 4, Owner: "other", Name: "c"),
                new(LineNumber: 4, Owner: "credfeto", Name: "d"),
            ],
            actual: references
        );
    }

    [Fact]
    public static void EmptyTextHasNoReferences()
    {
        Assert.Empty(RepositoryReferenceScanner.Scan(text: string.Empty, privateOwners: PrivateOwners));
    }

    [Fact]
    public static void ReferenceTextFormHoldsOnlyTheLineNumber()
    {
        RepositoryReference reference = new(LineNumber: 3, Owner: "secretorg", Name: "hidden");

        Assert.Equal(expected: "Line 3", actual: reference.ToString());
    }

    [Fact]
    public static void ReferenceRepositoryJoinsOwnerAndName()
    {
        RepositoryReference reference = new(LineNumber: 3, Owner: "secretorg", Name: "hidden");

        Assert.Equal(expected: "secretorg/hidden", actual: reference.Repository);
    }
}
