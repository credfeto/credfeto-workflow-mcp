using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpRepoFileToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string PATH = "README.md";
    private const string REF = "v1.0.0";

    private readonly IRepoFileService _service;

    public WorkflowMcpRepoFileToolsTests()
    {
        this._service = GetSubstitute<IRepoFileService>();
    }

    [Fact]
    public async Task RepoFileReturnsWhatTheServiceReadAsync()
    {
        RepoFileResponse expected = new(
            Kind: RepoFileKind.File,
            Message: "the file was read",
            Repository: REPOSITORY,
            Path: PATH,
            Ref: REF,
            Size: 5,
            Content: "hello",
            Entries: null
        );
        this.ReadReturnsAsync().Returns(expected);

        RepoFileResponse actual = await this.RepoFileAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoFileLeavesTheRefToTheServiceAsync()
    {
        RepoFileResponse expected = new(
            Kind: RepoFileKind.Directory,
            Message: "the directory was listed",
            Repository: REPOSITORY,
            Path: "src",
            Ref: null,
            Size: null,
            Content: null,
            Entries: [new RepoFileEntry(Type: "file", Name: "a.cs")]
        );
        this._service.ReadAsync(
                repository: REPOSITORY,
                path: "src",
                gitRef: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        RepoFileResponse actual = await WorkflowMcpTools.RepoFileAsync(
            repo: REPOSITORY,
            path: "src",
            service: this._service,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoFileFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoFileFailures())
        {
            this.ReadReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RepoFileAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoFileOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ReadReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoFileAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoFileFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "path", rule: "it must not contain control characters"),
            new RepoFileFailedException("could not read the content of " + REPOSITORY),
        ];
    }

    private ValueTask<RepoFileResponse> ReadReturnsAsync()
    {
        return this._service.ReadAsync(
            repository: REPOSITORY,
            path: PATH,
            gitRef: REF,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<RepoFileResponse> RepoFileAsync()
    {
        return WorkflowMcpTools.RepoFileAsync(
            repo: REPOSITORY,
            path: PATH,
            service: this._service,
            @ref: REF,
            cancellationToken: this.CancellationToken()
        );
    }
}
