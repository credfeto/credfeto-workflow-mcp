using System;
using System.Net.Http;
using FunFair.Test.Common;
using NSubstitute;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public abstract class GitHubClientTestBase : TestBase
{
    protected static IHttpClientFactory MockHttpClientFactoryCreateClient(HttpMessageHandler upstream)
    {
        return MockHttpClientFactoryCreateClient(upstream: upstream, baseAddress: GitHubServices.ApiBaseUrl);
    }

    protected static IHttpClientFactory MockHttpClientFactoryCreateClient(
        HttpMessageHandler upstream,
        string baseAddress
    )
    {
        IHttpClientFactory factory = GetSubstitute<IHttpClientFactory>();

        factory
            .CreateClient(GitHubSetup.HttpClientName)
            .Returns(_ => new HttpClient(handler: upstream, disposeHandler: false)
            {
                BaseAddress = new Uri(baseAddress),
            });

        return factory;
    }
}
