using System;
using System.Net.Http;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class BoardPermissionFailuresTests : TestBase
{
    [Fact]
    public void BoardThatIsNotFoundIsAPermissionFailure()
    {
        Assert.True(
            condition: BoardPermissionFailures.IsPermissionFailure(new WorkflowBoardNotFoundException("not found")),
            userMessage: "A board the caller cannot see is reported as not found"
        );
    }

    [Theory]
    [InlineData("FORBIDDEN", "Resource not accessible by integration")]
    [InlineData("INSUFFICIENT_SCOPES", "Your token has not been granted the required scopes")]
    [InlineData("NOT_FOUND", "Could not resolve to a ProjectV2 with the number 7.")]
    [InlineData("", "Could not resolve to a node with the global id of 'PVT_kwDOA'")]
    public static void RefusedGraphQlErrorIsAPermissionFailure(string type, string message)
    {
        GitHubGraphQlException exception = Refused(type: type, message: message);

        Assert.True(condition: BoardPermissionFailures.IsPermissionFailure(exception), userMessage: message);
    }

    [Theory]
    [InlineData("FORBIDDEN", "Resource not accessible by integration")]
    [InlineData("INSUFFICIENT_SCOPES", "Your token has not been granted the required scopes")]
    [InlineData("", "Could not resolve to a Repository with the name 'credfeto/private'.")]
    public static void WrappedRefusedGraphQlErrorIsAPermissionFailure(string type, string message)
    {
        GitHubGraphQlException inner = Refused(type: type, message: message);

        Assert.True(
            condition: BoardPermissionFailures.IsPermissionFailure(
                new WorkflowBoardInvalidException(message: "invalid", innerException: inner)
            ),
            userMessage: "Workflow board invalid"
        );
        Assert.True(
            condition: BoardPermissionFailures.IsPermissionFailure(
                new StatusFieldReadFailedException(message: "could not read", innerException: inner)
            ),
            userMessage: "Status field read failed"
        );
        Assert.True(
            condition: BoardPermissionFailures.IsPermissionFailure(
                new BoardWriteException(message: "could not add", innerException: inner)
            ),
            userMessage: "Board write"
        );
    }

    [Theory]
    [InlineData("NOT_FOUND", "Not found")]
    [InlineData("forbidden", "lower case types are not GitHub's")]
    [InlineData("RATE_LIMITED", "API rate limit exceeded")]
    [InlineData("", "could not resolve in lower case")]
    public static void OtherGraphQlErrorIsNotAPermissionFailure(string type, string message)
    {
        GitHubGraphQlException exception = Refused(type: type, message: message);

        Assert.False(condition: BoardPermissionFailures.IsPermissionFailure(exception), userMessage: message);
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(
                new BoardWriteException(message: "could not add", innerException: exception)
            ),
            userMessage: message
        );
    }

    [Fact]
    public void AmbiguousBoardIsNotAPermissionFailure()
    {
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(new WorkflowBoardAmbiguousException("ambiguous")),
            userMessage: "An ambiguous board was seen, so the caller can read it"
        );
    }

    [Fact]
    public void HttpFailureIsNotAPermissionFailure()
    {
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(new HttpRequestException("bad gateway")),
            userMessage: "An upstream failure is not a refusal"
        );
    }

    [Fact]
    public void WrapperWithoutAGraphQlErrorIsNotAPermissionFailure()
    {
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(
                new BoardWriteException(message: "could not add", innerException: new HttpRequestException("bad"))
            ),
            userMessage: "A wrapped upstream failure is not a refusal"
        );
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(new StatusFieldReadFailedException("no inner")),
            userMessage: "A wrapper with nothing inside is not a refusal"
        );
    }

    [Fact]
    public void UnrelatedExceptionIsNotAPermissionFailure()
    {
        Assert.False(
            condition: BoardPermissionFailures.IsPermissionFailure(new InvalidOperationException("Could not resolve")),
            userMessage: "Only GitHub's own errors are recognised"
        );
    }

    private static GitHubGraphQlException Refused(string type, string message)
    {
        return new(message) { Errors = [message], ErrorTypes = type.Length == 0 ? [] : [type] };
    }
}
