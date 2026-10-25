using System.Collections.Generic;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Json;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ServiceRegistrationTests : TestBase
{
    [Fact]
    public async Task AddWorkflowServicesRegistersMcpToolsAsync()
    {
        await using WebApplication app = TestAppFactory.BuildApp();

        IEnumerable<McpServerTool> tools = app.Services.GetServices<McpServerTool>();

        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task AddWorkflowServicesRegistersAppJsonContextAsync()
    {
        await using WebApplication app = TestAppFactory.BuildApp();

        JsonOptions options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;

        Assert.Contains(expected: AppJsonContext.Default, collection: options.SerializerOptions.TypeInfoResolverChain);
    }
}
