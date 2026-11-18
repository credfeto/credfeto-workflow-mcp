using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ToolModeEndpointTests : TestBase
{
    private const string LIST_TOOLS = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

    private const string CALL_PING =
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"ping","arguments":{}}}""";

    private const string CALL_PR_VIEW = """
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"pr_view","arguments":{"repo":"credfeto/credfeto-workflow-mcp","pr":1}}}
        """;

    private const string CALL_COMMENT_ADD_ON_PR = """
        {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"comment_add","arguments":{"repo":"credfeto/credfeto-workflow-mcp","body":"text","pr":1}}}
        """;

    private const int ALL_TOOL_COUNT = 52;

    private static readonly string[] IssuesTools =
    [
        "body_check",
        "comment_add",
        "issue_add_sub_issue",
        "issue_comments",
        "issue_create",
        "issue_edit",
        "issue_list",
        "issue_plan_status",
        "issue_search",
        "issue_view",
        "label_add",
        "label_list",
        "label_remove",
        "ping",
    ];

    [Fact]
    public async Task WithoutAModeEveryToolIsListedAsync()
    {
        IReadOnlyList<string> tools = await this.ListToolsAsync(null);

        Assert.Equal(expected: ALL_TOOL_COUNT, actual: tools.Count);
        Assert.Contains(expected: "pr_view", collection: tools, comparer: StringComparer.Ordinal);
        Assert.Contains(expected: "issue_close", collection: tools, comparer: StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Issues")]
    [InlineData("issues")]
    [InlineData("ISSUES")]
    public async Task IssuesModeListsExactlyTheIssueToolsAsync(string mode)
    {
        IReadOnlyList<string> tools = await this.ListToolsAsync(mode);

        Assert.Equal(expected: IssuesTools, actual: tools, comparer: StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("all")]
    public async Task AllModeListsEveryToolAsync(string mode)
    {
        IReadOnlyList<string> tools = await this.ListToolsAsync(mode);

        Assert.Equal(expected: ALL_TOOL_COUNT, actual: tools.Count);
    }

    [Theory]
    [InlineData("Issue")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("Issues, All")]
    public async Task AnUnknownModeIsRejectedAsync(string mode)
    {
        (HttpStatusCode status, string body) = await this.SendAsync(mode: mode, request: LIST_TOOLS);

        Assert.Equal(expected: HttpStatusCode.BadRequest, actual: status);
        Assert.Contains(
            expectedSubstring: "The X-Mode header must be one of: All, Issues",
            actualString: body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AnIssuesToolCanBeCalledInIssuesModeAsync()
    {
        (HttpStatusCode status, string body) = await this.SendAsync(mode: "Issues", request: CALL_PING);

        Assert.Equal(expected: HttpStatusCode.OK, actual: status);
        Assert.Contains(expectedSubstring: "pong", actualString: body, comparisonType: StringComparison.Ordinal);
    }

    [Fact]
    public async Task AToolOutsideTheModeIsRefusedNamingTheModeAsync()
    {
        using JsonDocument response = await this.CallAsync(mode: "Issues", request: CALL_PR_VIEW);

        JsonElement error = response.RootElement.GetProperty("error");
        Assert.Equal(expected: -32602, actual: error.GetProperty("code").GetInt32());
        Assert.StartsWith(
            expectedStartString: "The tool is not available in X-Mode Issues",
            actualString: error.GetProperty("message").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CommentAddOnAPullRequestIsRefusedInIssuesModeAsync()
    {
        using JsonDocument response = await this.CallAsync(mode: "Issues", request: CALL_COMMENT_ADD_ON_PR);

        string text = response.RootElement.GetRawText();
        Assert.Contains(
            expectedSubstring: "comment_add cannot comment on a pull request in X-Mode Issues",
            actualString: text,
            comparisonType: StringComparison.Ordinal
        );
    }

    private async Task<IReadOnlyList<string>> ListToolsAsync(string? mode)
    {
        using JsonDocument response = await this.CallAsync(mode: mode, request: LIST_TOOLS);

        return
        [
            .. response
                .RootElement.GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(static tool => tool.GetProperty("name").GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];
    }

    private async Task<JsonDocument> CallAsync(string? mode, string request)
    {
        (HttpStatusCode status, string body) = await this.SendAsync(mode: mode, request: request);

        Assert.Equal(expected: HttpStatusCode.OK, actual: status);

        return JsonDocument.Parse(JsonRpcMessage(body));
    }

    // The streamable HTTP transport may answer as a server-sent event; the JSON-RPC message is its data line
    private static string JsonRpcMessage(string body)
    {
        string? data = body.Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .FirstOrDefault(static line => line.StartsWith(value: "data:", comparisonType: StringComparison.Ordinal));

        return data is null ? body : data["data:".Length..].Trim();
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(string? mode, string request)
    {
        await using WebApplication app = TestAppFactory.BuildLoopbackApp();
        await app.StartAsync(this.CancellationToken());

        try
        {
            using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };
            using HttpRequestMessage message = new(
                method: HttpMethod.Post,
                requestUri: new Uri(uriString: "/mcp", uriKind: UriKind.Relative)
            );
            message.Headers.Accept.ParseAdd("application/json");
            message.Headers.Accept.ParseAdd("text/event-stream");
            message.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", parameter: "caller-token");

            if (mode is not null)
            {
                _ = message.Headers.TryAddWithoutValidation(name: "X-Mode", value: mode);
            }

            message.Content = new StringContent(
                content: request,
                encoding: Encoding.UTF8,
                mediaType: "application/json"
            );

            using HttpResponseMessage response = await client.SendAsync(
                request: message,
                cancellationToken: this.CancellationToken()
            );

            string body = await response.Content.ReadAsStringAsync(this.CancellationToken());

            return (response.StatusCode, body);
        }
        finally
        {
            await app.StopAsync(this.CancellationToken());
        }
    }
}
