using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class EndpointAuthorizationTests : TestBase
{
    private const string INITIALIZE_REQUEST = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0.0"}}}
        """;

    [Fact]
    public async Task PingIsAnonymousAsync()
    {
        HttpStatusCode statusCode = await this.SendAsync(method: HttpMethod.Get, path: "/ping", authorization: null);

        Assert.Equal(expected: HttpStatusCode.OK, actual: statusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Basic dXNlcjpwYXNz", HttpStatusCode.Unauthorized)]
    [InlineData("Bearer caller-token", HttpStatusCode.OK)]
    public async Task McpRequiresBearerTokenAsync(string? authorization, HttpStatusCode expected)
    {
        HttpStatusCode statusCode = await this.SendAsync(
            method: HttpMethod.Post,
            path: "/mcp",
            authorization: authorization
        );

        Assert.Equal(expected: expected, actual: statusCode);
    }

    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string path, string? authorization)
    {
        await using WebApplication app = BuildApp();
        await app.StartAsync(this.CancellationToken());

        try
        {
            using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };
            using HttpRequestMessage request = new(
                method: method,
                requestUri: new Uri(uriString: path, uriKind: UriKind.Relative)
            );
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");

            if (authorization is not null)
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
            }

            if (method == HttpMethod.Post)
            {
                request.Content = new StringContent(
                    content: INITIALIZE_REQUEST,
                    encoding: Encoding.UTF8,
                    mediaType: "application/json"
                );
            }

            using HttpResponseMessage response = await client.SendAsync(
                request: request,
                cancellationToken: this.CancellationToken()
            );

            return response.StatusCode;
        }
        finally
        {
            await app.StopAsync(this.CancellationToken());
        }
    }

    private static WebApplication BuildApp()
    {
        return TestAppFactory.BuildApp(ConfigureForLoopback).MapWorkflowEndpoints();
    }

    private static void ConfigureForLoopback(WebApplicationBuilder builder)
    {
        _ = builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>(key: "GitHub:ApiBaseUrl", value: "https://api.github.com/"),
        ]);

        // Port 0 binds an ephemeral loopback port so the test never collides with a fixed port
        _ = builder.WebHost.UseKestrel(static options => options.Listen(address: IPAddress.Loopback, port: 0));
    }
}
