using System.Net;
using System.Net.Http;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubLinkHeaderTests : TestBase
{
    [Fact]
    public void NoLinkHeaderHasNoNextPage()
    {
        using HttpResponseMessage response = Responses.Status(HttpStatusCode.OK);

        Assert.Null(GitHubLinkHeader.FindNextPage(response.Headers));
    }

    [Fact]
    public void LinkWithoutNextRelationHasNoNextPage()
    {
        using HttpResponseMessage response = Responses.Json(
            json: "[]",
            nextLink: "<https://api.github.com/repositories/1/issues?page=1>; rel=\"prev\", <https://api.github.com/repositories/1/issues?page=3>; rel=\"last\""
        );

        Assert.Null(GitHubLinkHeader.FindNextPage(response.Headers));
    }

    [Theory]
    [InlineData("<https://api.github.com/repositories/1/issues?page=2>; rel=\"next\"", "/repositories/1/issues?page=2")]
    [InlineData(
        "<https://api.github.com/user/repos?affiliation=owner,collaborator&page=1>; rel=\"prev\", <https://api.github.com/user/repos?affiliation=owner,collaborator&page=3>; rel=\"next\"",
        "/user/repos?affiliation=owner,collaborator&page=3"
    )]
    [InlineData("<https://api.github.com/items?page=2>; rel=\"last NEXT\"", "/items?page=2")]
    [InlineData("<items?page=2>; rel=\"next\"", "items?page=2")]
    public static void NextLinkIsRewrittenToPathAndQuery(string link, string expected)
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        Assert.Equal(expected: expected, actual: GitHubLinkHeader.FindNextPage(response.Headers));
    }
}
