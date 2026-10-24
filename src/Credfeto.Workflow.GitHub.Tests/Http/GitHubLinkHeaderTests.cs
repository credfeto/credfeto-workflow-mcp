using System;
using System.Net.Http;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubLinkHeaderTests : TestBase
{
    [Theory]
    [InlineData(null, GitHubServices.ApiBaseUrl, null)]
    [InlineData(
        "<https://api.github.com/repositories/1/issues?page=1>; rel=\"prev\", <https://api.github.com/repositories/1/issues?page=3>; rel=\"last\"",
        GitHubServices.ApiBaseUrl,
        null
    )]
    [InlineData(
        "<https://api.github.com/repositories/1/issues?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "repositories/1/issues?page=2"
    )]
    [InlineData(
        "<https://api.github.com/user/repos?affiliation=owner,collaborator&page=1>; rel=\"prev\", <https://api.github.com/user/repos?affiliation=owner,collaborator&page=3>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "user/repos?affiliation=owner,collaborator&page=3"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2>; rel=\"last NEXT\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2>; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2>; REL=Next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2>;rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2> ; rel = \"next\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://api.github.com/items?page=1>; rel=prev, <https://api.github.com/items?page=3>; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=3"
    )]
    [InlineData(
        "<https://api.github.com/items?page=2>; type=\"text/html\"; rel=next; title=page",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData("<https://api.github.com/items?page=2>; foorel=\"next\"", GitHubServices.ApiBaseUrl, null)]
    [InlineData("<https://api.github.com/items?page=2>; foorel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData(
        "<https://api.github.com/items?page=2>; title=\"a; rel=next\"; rel=prev",
        GitHubServices.ApiBaseUrl,
        null
    )]
    [InlineData("<https://api.github.com/items?page=2>; rel=prev; rel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData(
        "<https://api.github.com/x?page=1>; title=\"<items?page=99>; rel=next\"",
        GitHubServices.ApiBaseUrl,
        null
    )]
    [InlineData(
        "<https://api.github.com/x?page=1>; title=\"a, <items?page=99>; rel=next\"",
        GitHubServices.ApiBaseUrl,
        null
    )]
    [InlineData(
        "<https://api.github.com/items?page=1>; title=\"a, b\", <https://api.github.com/items?page=3>; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=3"
    )]
    [InlineData(
        "<https://api.github.com/x?page=1>; title=\"x\\\"<items?page=99>; rel=next\\\"\"",
        GitHubServices.ApiBaseUrl,
        null
    )]
    [InlineData(
        "<https://api.github.com/x\\>; rel=prev, <https://api.github.com/items?page=2>; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData("<https://api.github.com/items?page=2>; rel=\"n\\ext\"", GitHubServices.ApiBaseUrl, null)]
    [InlineData(
        "<https://api.github.com/items?page=2>; crossorigin; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        ", <https://api.github.com/items?page=2>; rel=next",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData("<https://api.github.com/items?page=2; rel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData("<https://api.github.com/items?page=2>; rel=\"next", GitHubServices.ApiBaseUrl, null)]
    [InlineData("<https://api.github.com/x>; title=\"a, <items?page=2>; rel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData("https://api.github.com/items?page=2; rel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData("<https://api.github.com/items>?page=2>; rel=next", GitHubServices.ApiBaseUrl, null)]
    [InlineData("<items?page=2>; rel=\"next\"", GitHubServices.ApiBaseUrl, GitHubServices.ApiBaseUrl + "items?page=2")]
    [InlineData(
        "<https://github-proxy.example/items?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "items?page=2"
    )]
    [InlineData(
        "<https://ghe.example/api/v3/repositories/9/issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "<https://ghe.example/api/v3/repositories/9/issues?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "<https://api.github.com/repositories/9/issues?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "<https://ghe.example/API/V3/repositories/9/issues?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3/API/V3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "<https://ghe.example/api/v3x/repositories/9/issues?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3/api/v3x/repositories/9/issues?page=2"
    )]
    [InlineData(
        "</api/v3/repositories/9/issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "</repositories/9/issues?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "repositories/9/issues?page=2"
    )]
    [InlineData(
        "<https://proxy.example/api/v3//repos/o/r/issues?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3//repos/o/r/issues?page=2"
    )]
    [InlineData(
        "<https://github-proxy.example///x?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "https://github-proxy.example///x?page=2"
    )]
    public static void NextLinkIsResolvedUnderBaseAddress(string? link, string baseAddress, string? expected)
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        // Compared as text so that a rewrite such as collapsing an empty path segment is caught
        Assert.Equal(
            expected: expected,
            actual: GitHubLinkHeader
                .FindNextPage(
                    headers: response.Headers,
                    requestUri: FirstPageUnder(baseAddress),
                    baseAddress: new Uri(baseAddress)
                )
                ?.AbsoluteUri
        );
    }

    [Theory]
    [InlineData(
        "<issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/issues?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/r/issues?page=2"
    )]
    [InlineData(
        "<?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/issues?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/r/issues?page=2"
    )]
    [InlineData(
        "<../issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/issues?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/issues?page=2"
    )]
    [InlineData(
        "<files?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/pulls/7/files?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/r/pulls/7/files?page=2"
    )]
    [InlineData(
        "<issues?page=2>; rel=\"next\"",
        "https://github-proxy.example/repos/o/r/issues",
        GitHubServices.ApiBaseUrl,
        GitHubServices.ApiBaseUrl + "repos/o/r/issues?page=2"
    )]
    [InlineData(
        "<my%20repo%2ename/issues?q=repo%3Aowner%2Frepo&page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/issues?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/r/my%20repo.name/issues?q=repo%3Aowner%2Frepo&page=2"
    )]
    [InlineData(
        "<labels/area%2Fapi?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/repos/o/r/issues?page=1",
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repos/o/r/labels/area%2Fapi?page=2"
    )]
    public static void RelativeNextLinkIsResolvedAgainstRequestUri(
        string link,
        string requestUri,
        string baseAddress,
        string expected
    )
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        Assert.Equal(
            expected: expected,
            actual: GitHubLinkHeader
                .FindNextPage(
                    headers: response.Headers,
                    requestUri: new Uri(requestUri),
                    baseAddress: new Uri(baseAddress)
                )
                ?.AbsoluteUri
        );
    }

    [Theory]
    [InlineData("<../../../../users?page=2>; rel=\"next\"", "https://ghe.example/api/v3/repos/o/r/issues")]
    [InlineData("<../../../../../issues?page=2>; rel=\"next\"", "https://ghe.example/api/v3/repos/o/r/issues")]
    [InlineData("<issues?page=2>; rel=\"next\"", "https://other.example/api/v3/repos/o/r/issues")]
    public static void RelativeNextLinkEscapingBasePathFromRequestUriIsInvalidResponse(string link, string requestUri)
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        GitHubInvalidResponseException exception = Assert.Throws<GitHubInvalidResponseException>(() =>
            GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: new Uri(requestUri),
                baseAddress: new Uri("https://ghe.example/api/v3/")
            )
        );

        Assert.Equal(
            expected: "GitHub returned a next page link outside the configured GitHub API host and base path",
            actual: exception.Message
        );
    }

    private static Uri FirstPageUnder(string baseAddress)
    {
        return new(baseUri: new Uri(baseAddress), relativeUri: "items?page=1");
    }

    [Theory]
    [InlineData(
        "<ftp://evil.example/items?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "GitHub returned a next page link that is not an HTTP(S) URI"
    )]
    [InlineData(
        "<//elsewhere.example/items?page=2>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "GitHub returned a next page link that is not an HTTP(S) URI"
    )]
    [InlineData(
        "<http://[>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "GitHub returned a next page link that is not a valid URI"
    )]
    [InlineData(
        "<http://exa mple/items>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "GitHub returned a next page link that is not a valid URI"
    )]
    [InlineData(
        "<http://>; rel=\"next\"",
        GitHubServices.ApiBaseUrl,
        "GitHub returned a next page link that is not a valid URI"
    )]
    [InlineData(
        "<https://ghe.example/repositories/9/issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "</repositories/9/issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<../repositories/9/issues?page=2>; rel=\"next\"",
        "https://ghe.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<https://ghe.example//elsewhere.example/items?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<https://ghe.example/a:b/x?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<https://ghe.example/repos:x/items?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<%2e%2e/%2e%2e/admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<%2E%2e/admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<.%2e/admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<%2e./admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<items%2f..%2f..%2fadmin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<items%5C..%5C..%5Cadmin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<https://proxy.example/api/v3/%2e%2e/%2e%2e/admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<items%252f..%252f..%252fadmin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<items%255C..%255C..%255Cadmin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<%25252e%25252e/admin?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    [InlineData(
        "<items/a%25252541?page=2>; rel=\"next\"",
        "https://proxy.example/api/v3/",
        "GitHub returned a next page link outside the configured GitHub API host and base path"
    )]
    public static void MalformedNextLinkIsInvalidResponse(string link, string baseAddress, string expectedMessage)
    {
        using HttpResponseMessage response = Responses.Json(json: "[]", nextLink: link);

        GitHubInvalidResponseException exception = Assert.Throws<GitHubInvalidResponseException>(() =>
            GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: FirstPageUnder(baseAddress),
                baseAddress: new Uri(baseAddress)
            )
        );

        Assert.Equal(expected: expectedMessage, actual: exception.Message);
    }
}
