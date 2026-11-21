using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

// Compares tools by name rather than by count, so a missing or extra tool fails naming it and adding a tool needs no
// shared number to be edited
internal static class ToolNameAssert
{
    public static void SameTools(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        string[] missing =
        [
            .. expected.Except(second: actual, comparer: StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
        string[] unexpected =
        [
            .. actual.Except(second: expected, comparer: StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
        string[] duplicated =
        [
            .. actual
                .GroupBy(keySelector: static name => name, comparer: StringComparer.Ordinal)
                .Where(static group => group.Skip(1).Any())
                .Select(static group => group.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0 && duplicated.Length == 0,
            userMessage: $"Missing tools: [{Join(missing)}]; unexpected tools: [{Join(unexpected)}]; duplicated tools: [{Join(duplicated)}]"
        );
    }

    private static string Join(IEnumerable<string> names)
    {
        return string.Join(separator: ", ", values: names);
    }
}
