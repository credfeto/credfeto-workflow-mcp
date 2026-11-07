using System;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Runs;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunLogStorageTests : TestBase
{
    [Theory]
    [InlineData("https://results-receiver.actions.githubusercontent.com/rest/logs.zip?sig=abc")]
    [InlineData("https://productionresultssa0.blob.core.windows.net/actions-results/logs.zip?sig=abc")]
    [InlineData("https://productionresultssa17.blob.core.windows.net/actions-results/logs.zip?sig=abc")]
    [InlineData("https://PRODUCTIONRESULTSSA3.blob.core.windows.net/logs.zip")]
    [InlineData("https://results-receiver.actions.githubusercontent.com:443/logs.zip")]
    public static void StorageLocationIsAccepted(string location)
    {
        Uri uri = new(location);

        Assert.Same(expected: uri, actual: RunLogStorage.Validate(uri));
    }

    [Theory]
    [InlineData("http://results-receiver.actions.githubusercontent.com/logs.zip")]
    [InlineData("https://user@results-receiver.actions.githubusercontent.com/logs.zip")]
    [InlineData("https://user:pw@results-receiver.actions.githubusercontent.com/logs.zip")]
    [InlineData("https://results-receiver.actions.githubusercontent.com:8443/logs.zip")]
    [InlineData("https://example.com/logs.zip")]
    [InlineData("https://results-receiver.actions.githubusercontent.com.example.com/logs.zip")]
    [InlineData("https://productionresultssa.blob.core.windows.net/logs.zip")]
    [InlineData("https://productionresultssa1x.blob.core.windows.net/logs.zip")]
    [InlineData("https://evil.productionresultssa1.blob.core.windows.net/logs.zip")]
    [InlineData("https://productionresultssa1.blob.core.windows.net.example.com/logs.zip")]
    [InlineData("ftp://results-receiver.actions.githubusercontent.com/logs.zip")]
    public static void LocationOffTheStorageHostsIsRefused(string location)
    {
        RunLogFailedException exception = Assert.Throws<RunLogFailedException>(() =>
            RunLogStorage.Validate(new Uri(location))
        );

        Assert.StartsWith(
            expectedStartString: "GitHub returned a log location on an unexpected host",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "logs.zip",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public void RelativeLocationIsRefused()
    {
        RunLogFailedException exception = Assert.Throws<RunLogFailedException>(() =>
            RunLogStorage.Validate(new Uri("/logs.zip", UriKind.Relative))
        );

        Assert.Equal(expected: "GitHub returned a log location that is not absolute", actual: exception.Message);
    }
}
