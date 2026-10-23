using System;
using System.Net;
using System.Net.Http;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubLinkHeaderTests : TestBase
{
    private static readonly Uri BaseAddress = new(GitHubServices.ApiBaseUrl);

    [Fact]
    public void NoLinkHeaderHasNoNextPage()
    {
        using HttpResponseMessage response = Responses.Status(HttpStatusCode.OK);

        Assert.Null(GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: BaseAddress));
    }

    [Fact]
    public void LinkWithoutNextRelationHasNoNextPage()
    {
        using HttpResponseMessage response = Responses.Json(
            json: "[]",
            nextLink: "<https://api.github.com/repositories/1/issues?page=1>; rel=\"prev\", <https://api.github.com/repositories/1/issues?page=3>; rel=\"last\""
        );

        Assert.Null(GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: BaseAddress));
    }

    [Theory]
    [InlineData("<https://api.github.com/repositories/1/issues?page=2>; rel=\"next\"", "repositories/1/issues?page=2")]
    [InlineData(
        "<https://api.github.com/user/repos?affiliation=owner,collaborator&page=1>; rel=\"prev\", <https://api.github.com/user/repos?affiliation=owner,collaborator&page=3>; rel=\"next\"",
        "user/repos?affiliation=owner,collaborator&page=3"
    )]
    [InlineData("<https://api.github.com/items?page=2>; rel=\"last NEXT\"", "items?page=2")]
    [InlineData("<items?page=2>; rel=\"next\"", "items?page=2")]
    [InlineData("<https://github-proxy.example/items?page=2>; rel=\"next\"", "/items?page=2")]
    [InlineData("<//elsewhere.example/items?page=2>; rel=\"next\"", "//elsewhere.example/items?page=2")]
    public static void NextLinkIsRewrittenRelativeToBaseAddress(string link, string expected)
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        Assert.Equal(
            expected: expected,
            actual: GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: BaseAddress)
        );
    }

    [Fact]
    public void NextLinkOnSameServerKeepsItsBasePath()
    {
        using HttpResponseMessage response = Responses.Json(
            json: "[]",
            nextLink: "<https://ghe.example/api/v3/repositories/9/issues?page=2>; rel=\"next\""
        );

        Assert.Equal(
            expected: "/api/v3/repositories/9/issues?page=2",
            actual: GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                baseAddress: new Uri("https://ghe.example/api/v3/")
            )
        );
    }

    [Fact]
    public void NextLinkWithoutBaseAddressIsMadeRelative()
    {
        using HttpResponseMessage response = Responses.Json(
            json: "[]",
            nextLink: "<https://api.github.com/repositories/9/issues?page=2>; rel=\"next\""
        );

        Assert.Equal(
            expected: "repositories/9/issues?page=2",
            actual: GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: null)
        );
    }
}
