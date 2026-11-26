using Credfeto.Workflow.GitHub.BodyRules;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.BodyRules;

public sealed class EnvBlockMarkerTests : TestBase
{
    private const string MARKER = "<!-- orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef01234567 -->";

    [Theory]
    [InlineData("Which image should this use?")]
    [InlineData("")]
    [InlineData("orchestrator env-block")]
    public static void ABodyWithoutAMarkerHasNoMarkerLine(string body)
    {
        Assert.Null(EnvBlockMarker.FindMarkerLine(body));
        Assert.Null(EnvBlockMarker.FindMalformedMarkerLine(body));
    }

    [Theory]
    [InlineData(MARKER, 1)]
    [InlineData("first\n" + MARKER, 2)]
    [InlineData("first\r\nsecond\r\n<!-- ORCHESTRATOR:ENV-BLOCK -->\r\n", 3)]
    [InlineData("a\nb\nsee Orchestrator:Env-Block\n" + MARKER, 3)]
    public static void AnyMarkerIsFoundOnTheFirstLineHoldingIt(string body, int expected)
    {
        Assert.Equal(expected: expected, actual: EnvBlockMarker.FindMarkerLine(body));
    }

    [Theory]
    [InlineData(MARKER)]
    [InlineData("text " + MARKER + " text")]
    [InlineData(MARKER + "\n" + MARKER)]
    [InlineData(MARKER + MARKER)]
    [InlineData(MARKER + "\r\nmore")]
    public static void ExactMarkersAreNotMalformed(string body)
    {
        Assert.Null(EnvBlockMarker.FindMalformedMarkerLine(body));
    }

    [Theory]
    [InlineData("<!-- orchestrator:env-block -->", 1)]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789ABCDEF0123456789abcdef01234567 -->", 1)]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef0123456 -->", 1)]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef012345678 -->", 1)]
    [InlineData("<!--orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef01234567-->", 1)]
    [InlineData("<!-- ORCHESTRATOR:ENV-BLOCK image-sha=0123456789abcdef0123456789abcdef01234567 -->", 1)]
    [InlineData(MARKER + "\nfine\n" + MARKER + " orchestrator:env-block", 3)]
    public static void AMarkerThatIsNotExactIsMalformedOnItsLine(string body, int expected)
    {
        Assert.Equal(expected: expected, actual: EnvBlockMarker.FindMalformedMarkerLine(body));
    }
}
