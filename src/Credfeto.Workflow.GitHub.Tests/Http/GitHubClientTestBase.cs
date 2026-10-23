using System;
using System.Net.Http;
using FunFair.Test.Common;
using NSubstitute;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public abstract class GitHubClientTestBase : TestBase
{
    protected static IHttpClientFactory MockHttpClientFactoryCreateClient(HttpMessageHandler upstream)
    {
        IHttpClientFactory factory = GetSubstitute<IHttpClientFactory>();

        factory
            .CreateClient(GitHubSetup.HttpClientName)
            .Returns(_ => new HttpClient(handler: upstream, disposeHandler: false)
            {
                BaseAddress = new Uri(GitHubServices.ApiBaseUrl),
            });

        return factory;
    }
}
