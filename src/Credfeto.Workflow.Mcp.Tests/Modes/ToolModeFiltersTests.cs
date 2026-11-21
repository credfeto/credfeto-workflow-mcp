using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Modes;

public sealed class ToolModeFiltersTests : TestBase
{
    private const string ISSUE_TOOL = "issue_view";

    private const string OTHER_ISSUE_TOOL = "issue_comments";

    private const string PULL_REQUEST_TOOL = "pr_view";

    [Fact]
    public async Task EveryToolIsListedWithoutAModeAsync()
    {
        IReadOnlyList<string> listed = await this.ListAsync(
            mode: ToolModes.All,
            names: [PULL_REQUEST_TOOL, ISSUE_TOOL]
        );

        Assert.Equal(expected: [ISSUE_TOOL, PULL_REQUEST_TOOL], actual: listed);
    }

    [Fact]
    public async Task OnlyTheModesToolsAreListedAsync()
    {
        IReadOnlyList<string> listed = await this.ListAsync(
            mode: ToolModes.Issues,
            names: [PULL_REQUEST_TOOL, ISSUE_TOOL]
        );

        Assert.Equal(expected: [ISSUE_TOOL], actual: listed);
    }

    [Fact]
    public async Task ToolsAreListedInNameOrderIgnoringCaseAsync()
    {
        IReadOnlyList<string> listed = await this.ListAsync(
            mode: ToolModes.All,
            names: ["run_view", "Pr_list", "auth_status", "pr_diff", "Label_add"]
        );

        Assert.Equal(expected: ["auth_status", "Label_add", "pr_diff", "Pr_list", "run_view"], actual: listed);
    }

    [Fact]
    public async Task ModeToolsAreListedInNameOrderAsync()
    {
        IReadOnlyList<string> listed = await this.ListAsync(
            mode: ToolModes.Issues,
            names: [PULL_REQUEST_TOOL, OTHER_ISSUE_TOOL, ISSUE_TOOL]
        );

        Assert.Equal(expected: [OTHER_ISSUE_TOOL, ISSUE_TOOL], actual: listed);
    }

    [Theory]
    [InlineData(ToolModes.All, ISSUE_TOOL)]
    [InlineData(ToolModes.All, PULL_REQUEST_TOOL)]
    [InlineData(ToolModes.All, "no_such_tool")]
    [InlineData(ToolModes.Issues, ISSUE_TOOL)]
    public async Task AToolInTheModeIsCalledAsync(ToolModes mode, string name)
    {
        bool called = await this.CallAsync(mode: mode, parameters: new() { Name = name });

        Assert.True(condition: called, userMessage: name + " should be called");
    }

    [Theory]
    [InlineData(PULL_REQUEST_TOOL)]
    [InlineData("no_such_tool")]
    public Task AToolOutsideTheModeIsRefusedBeforeItRunsAsync(string name)
    {
        return this.AssertRefusedAsync(new() { Name = name });
    }

    [Fact]
    public async Task ListingWithoutServicesFailsClosedAsync()
    {
        await using TestServer server = new();
        RequestContext<ListToolsRequestParams> context = new(
            server.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsList, Id = new RequestId(1) },
            new ListToolsRequestParams()
        );

        McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = ToolModeFilters.ListTools(
            static (_, _) => ValueTask.FromResult(new ListToolsResult())
        );

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await handler(context, this.CancellationToken())
        );
    }

    [Fact]
    public async Task CallingWithoutServicesFailsClosedAsync()
    {
        await using TestServer server = new();
        RequestContext<CallToolRequestParams> context = new(
            server.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall, Id = new RequestId(1) },
            new CallToolRequestParams { Name = ISSUE_TOOL }
        );

        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = ToolModeFilters.CallTool(
            static (_, _) => ValueTask.FromResult(new CallToolResult())
        );

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await handler(context, this.CancellationToken())
        );
    }

    private async Task AssertRefusedAsync(CallToolRequestParams parameters)
    {
        bool called = false;
        await using TestServer server = new();
        RequestContext<CallToolRequestParams> context = new(
            server.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall, Id = new RequestId(1) },
            parameters
        )
        {
            Services = Services(ToolModes.Issues),
        };

        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = ToolModeFilters.CallTool(
            (_, _) =>
            {
                called = true;

                return ValueTask.FromResult(new CallToolResult());
            }
        );

        McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            _ = await handler(context, this.CancellationToken())
        );

        Assert.False(condition: called, userMessage: "a refused tool must not run");
        Assert.Equal(expected: McpErrorCode.InvalidParams, actual: exception.ErrorCode);
        Assert.Equal(
            expected: "The tool is not available in X-Mode Issues; the Issues tools are: "
                + OTHER_ISSUE_TOOL
                + ", "
                + ISSUE_TOOL,
            actual: exception.Message
        );
    }

    private async Task<IReadOnlyList<string>> ListAsync(ToolModes mode, IReadOnlyList<string> names)
    {
        await using TestServer server = new();
        RequestContext<ListToolsRequestParams> context = new(
            server.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsList, Id = new RequestId(1) },
            new ListToolsRequestParams()
        )
        {
            Services = Services(mode),
        };

        McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = ToolModeFilters.ListTools(
            (_, _) =>
                ValueTask.FromResult(
                    new ListToolsResult { Tools = [.. names.Select(static name => new Tool { Name = name })] }
                )
        );

        ListToolsResult result = await handler(context, this.CancellationToken());

        return [.. result.Tools.Select(static tool => tool.Name)];
    }

    private async Task<bool> CallAsync(ToolModes mode, CallToolRequestParams parameters)
    {
        bool called = false;
        await using TestServer server = new();
        RequestContext<CallToolRequestParams> context = new(
            server.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall, Id = new RequestId(1) },
            parameters
        )
        {
            Services = Services(mode),
        };

        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = ToolModeFilters.CallTool(
            (_, _) =>
            {
                called = true;

                return ValueTask.FromResult(new CallToolResult());
            }
        );

        _ = await handler(context, this.CancellationToken());

        return called;
    }

    private static ServiceProvider Services(ToolModes mode)
    {
        return new ServiceCollection()
            .AddSingleton<IRequestToolMode>(new FixedToolMode(mode))
            .AddSingleton(
                new ToolModeCatalog([
                    CreateTool(name: ISSUE_TOOL, modes: ToolModes.All | ToolModes.Issues),
                    CreateTool(name: OTHER_ISSUE_TOOL, modes: ToolModes.All | ToolModes.Issues),
                    CreateTool(name: PULL_REQUEST_TOOL, modes: ToolModes.All),
                ])
            )
            .BuildServiceProvider();
    }

    private static McpServerTool CreateTool(string name, ToolModes modes)
    {
        return McpServerTool.Create(
            (Func<string>)(static () => "ok"),
            new McpServerToolCreateOptions { Name = name, Metadata = [new InModesAttribute(modes)] }
        );
    }

    private sealed class FixedToolMode : IRequestToolMode
    {
        public FixedToolMode(ToolModes current)
        {
            this.Current = current;
        }

        public ToolModes Current { get; }
    }

    // The filters only need a server to build a request context; it is never run
    private sealed class TestServer : IAsyncDisposable
    {
        private readonly StreamServerTransport _transport;

        public TestServer()
        {
            this._transport = new(
                inputStream: Stream.Null,
                outputStream: Stream.Null,
                serverName: null,
                loggerFactory: null
            );
            this.Server = McpServer.Create(
                transport: this._transport,
                serverOptions: new McpServerOptions(),
                loggerFactory: null,
                serviceProvider: null
            );
        }

        public McpServer Server { get; }

        public async ValueTask DisposeAsync()
        {
            await this.Server.DisposeAsync();
            await this._transport.DisposeAsync();
        }
    }
}
