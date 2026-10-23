using System;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using FunFair.Test.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Configuration;

public sealed class GitHubOptionsBindingTests : TestBase
{
    [Fact]
    public async Task ApiBaseUrlIsBoundFromGitHubSectionAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        GitHubOptions options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;

        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl), actual: options.ApiBaseUrl);
    }

    [Fact]
    public async Task InvalidApiBaseUrlFailsValidationAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(apiBaseUrl: "https://github.example/api/v3");

        IOptions<GitHubOptions> options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }
}
