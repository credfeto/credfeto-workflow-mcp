using System.Text;
using Credfeto.Workflow.Mcp.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Validation;

public sealed class InvisibleCharactersTests : TestBase
{
    [Theory]
    [InlineData(0x00A0)]
    [InlineData(0x00AD)]
    [InlineData(0x034F)]
    [InlineData(0x061C)]
    [InlineData(0x115F)]
    [InlineData(0x1160)]
    [InlineData(0x17B4)]
    [InlineData(0x17B5)]
    [InlineData(0x180B)]
    [InlineData(0x180F)]
    [InlineData(0x2007)]
    [InlineData(0x200B)]
    [InlineData(0x200F)]
    [InlineData(0x202A)]
    [InlineData(0x202E)]
    [InlineData(0x202F)]
    [InlineData(0x2060)]
    [InlineData(0x206F)]
    [InlineData(0x2800)]
    [InlineData(0x3164)]
    [InlineData(0xFE00)]
    [InlineData(0xFE0F)]
    [InlineData(0xFEFF)]
    [InlineData(0xFFA0)]
    [InlineData(0xFFF0)]
    [InlineData(0xFFF8)]
    [InlineData(0x1BCA0)]
    [InlineData(0x1BCA3)]
    [InlineData(0x1D173)]
    [InlineData(0x1D17A)]
    [InlineData(0xE0000)]
    [InlineData(0xE0FFF)]
    public static void ContainsEveryRangeBoundary(int codePoint)
    {
        Assert.True(
            condition: InvisibleCharacters.Contains(new Rune(codePoint)),
            userMessage: $"U+{codePoint:X4} should be invisible"
        );
    }

    [Theory]
    [InlineData(0x0020)]
    [InlineData(0x0041)]
    [InlineData(0x009F)]
    [InlineData(0x00A1)]
    [InlineData(0x00AC)]
    [InlineData(0x00AE)]
    [InlineData(0x034E)]
    [InlineData(0x0350)]
    [InlineData(0x061B)]
    [InlineData(0x061D)]
    [InlineData(0x115E)]
    [InlineData(0x1161)]
    [InlineData(0x17B3)]
    [InlineData(0x17B6)]
    [InlineData(0x180A)]
    [InlineData(0x1810)]
    [InlineData(0x2006)]
    [InlineData(0x2008)]
    [InlineData(0x200A)]
    [InlineData(0x2010)]
    [InlineData(0x2029)]
    [InlineData(0x2030)]
    [InlineData(0x205F)]
    [InlineData(0x2070)]
    [InlineData(0x27FF)]
    [InlineData(0x2801)]
    [InlineData(0x3000)]
    [InlineData(0x3163)]
    [InlineData(0x3165)]
    [InlineData(0xFDFF)]
    [InlineData(0xFE10)]
    [InlineData(0xFEFE)]
    [InlineData(0xFF00)]
    [InlineData(0xFF9F)]
    [InlineData(0xFFA1)]
    [InlineData(0xFFEF)]
    [InlineData(0xFFF9)]
    [InlineData(0x1BC9F)]
    [InlineData(0x1BCA4)]
    [InlineData(0x1D172)]
    [InlineData(0x1D17B)]
    [InlineData(0x1F600)]
    [InlineData(0xDFFFF)]
    [InlineData(0xE1000)]
    public static void DoesNotContainNeighbouringCodePoints(int codePoint)
    {
        Assert.False(
            condition: InvisibleCharacters.Contains(new Rune(codePoint)),
            userMessage: $"U+{codePoint:X4} should not be invisible"
        );
    }
}
