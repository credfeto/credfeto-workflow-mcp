using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Workflows;

// Reads only the "on" section of a workflow, in the forms GitHub documents, rather than parsing the whole YAML. Any
// form it does not recognise reads as not declared, so a workflow it misreads is refused rather than dispatched
public static class WorkflowDispatchDeclaration
{
    private const char BYTE_ORDER_MARK = (char)0xFEFF;
    private const char COMMENT = '#';
    private const char DOUBLE_QUOTE = '"';
    private const char SINGLE_QUOTE = '\'';
    private const char SEQUENCE_ITEM = '-';

    private const string ON_KEY = "on";
    private const string DISPATCH_KEY = "workflow_dispatch";
    private const string INPUTS_KEY = "inputs";

    // Shared because an empty set is never modified, so every declaration without inputs can return the same one
    private static readonly FrozenSet<string> NoInputNames = [with(StringComparer.Ordinal)];

    // The declared input names, or null when workflow_dispatch is not declared in a form that can be read
    public static IReadOnlySet<string>? TryRead(string text)
    {
        IReadOnlyList<YamlLine>? lines = ReadLines(text);

        if (lines is null)
        {
            return null;
        }

        for (int index = 0; index < lines.Count; ++index)
        {
            if (
                lines[index].Indent == 0
                && TryReadKey(content: lines[index].Content, out string key, out string value)
                && StringComparer.Ordinal.Equals(x: key, y: ON_KEY)
            )
            {
                return value.Length == 0 ? ReadEventBlock(lines: lines, parent: index) : ReadInlineEvents(value);
            }
        }

        return null;
    }

    private static IReadOnlySet<string>? ReadInlineEvents(string value)
    {
        if (value[0] == '[' && value[^1] == ']')
        {
            return value[1..^1]
                .Split(',')
                .Select(item => Unquote(item.Trim()))
                .Contains(DISPATCH_KEY, StringComparer.Ordinal)
                ? NoInputs()
                : null;
        }

        return StringComparer.Ordinal.Equals(x: Unquote(value), y: DISPATCH_KEY) ? NoInputs() : null;
    }

    private static IReadOnlySet<string>? ReadEventBlock(IReadOnlyList<YamlLine> lines, int parent)
    {
        IReadOnlyList<int>? children = Children(lines: lines, parent: parent);

        if (children is null)
        {
            return null;
        }

        foreach (int child in children)
        {
            string content = lines[child].Content;

            if (IsSequenceItem(content))
            {
                if (StringComparer.Ordinal.Equals(x: Unquote(content[1..].Trim()), y: DISPATCH_KEY))
                {
                    return NoInputs();
                }
            }
            else if (
                TryReadKey(content: content, out string key, out string value)
                && StringComparer.Ordinal.Equals(x: key, y: DISPATCH_KEY)
            )
            {
                return ReadDispatch(lines: lines, parent: child, value: value);
            }
        }

        return null;
    }

    private static IReadOnlySet<string>? ReadDispatch(IReadOnlyList<YamlLine> lines, int parent, string value)
    {
        if (value.Length != 0)
        {
            return IsEmptyValue(value) ? NoInputs() : null;
        }

        IReadOnlyList<int>? children = Children(lines: lines, parent: parent);

        if (children is null)
        {
            return null;
        }

        foreach (int child in children)
        {
            if (
                TryReadKey(content: lines[child].Content, out string key, out string inputs)
                && StringComparer.Ordinal.Equals(x: key, y: INPUTS_KEY)
            )
            {
                return ReadInputs(lines: lines, parent: child, value: inputs);
            }
        }

        return NoInputs();
    }

    private static IReadOnlySet<string>? ReadInputs(IReadOnlyList<YamlLine> lines, int parent, string value)
    {
        if (value.Length != 0)
        {
            return IsEmptyValue(value) ? NoInputs() : null;
        }

        IReadOnlyList<int>? children = Children(lines: lines, parent: parent);

        if (children is null)
        {
            return null;
        }

        HashSet<string> names = [with(StringComparer.Ordinal)];

        foreach (int child in children)
        {
            if (
                !TryReadKey(content: lines[child].Content, out string name, out _)
                || name.Length == 0
                || !names.Add(name)
            )
            {
                return null;
            }
        }

        return names;
    }

    // The lines directly below the parent; null when a line sits between the parent's indent and the children's,
    // which is not valid YAML
    private static IReadOnlyList<int>? Children(IReadOnlyList<YamlLine> lines, int parent)
    {
        int parentIndent = lines[parent].Indent;
        int first = parent + 1;

        if (first >= lines.Count || lines[first].Indent <= parentIndent)
        {
            return [];
        }

        int childIndent = lines[first].Indent;
        List<int> children = [];

        for (int index = first; index < lines.Count && lines[index].Indent > parentIndent; ++index)
        {
            if (lines[index].Indent < childIndent)
            {
                return null;
            }

            if (lines[index].Indent == childIndent)
            {
                children.Add(index);
            }
        }

        return children;
    }

    // Blank and comment-only lines are dropped; null when a line is indented with a tab, which YAML does not allow
    private static IReadOnlyList<YamlLine>? ReadLines(string text)
    {
        List<YamlLine> lines = [];

        foreach (string line in text.TrimStart(BYTE_ORDER_MARK).Split('\n'))
        {
            string content = StripComment(line.TrimEnd('\r')).TrimEnd();
            int indent = content.Length - content.TrimStart(' ').Length;

            if (indent == content.Length)
            {
                continue;
            }

            if (content[indent] == '\t')
            {
                return null;
            }

            lines.Add(new(Indent: indent, Content: content[indent..]));
        }

        return lines;
    }

    // A '#' starts a comment only at the start of a line or after a space, and never inside a quoted scalar
    private static string StripComment(string line)
    {
        char quote = '\0';
        bool escaped = false;

        for (int index = 0; index < line.Length; ++index)
        {
            char current = line[index];

            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\' && quote == DOUBLE_QUOTE)
                {
                    escaped = true;
                }
                else if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            bool startsToken = index == 0 || line[index - 1] is ' ' or '\t' or '[' or '{' or ',';

            if (startsToken && current is DOUBLE_QUOTE or SINGLE_QUOTE)
            {
                quote = current;
            }
            else if (current == COMMENT && (index == 0 || line[index - 1] is ' ' or '\t'))
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool TryReadKey(string content, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (IsSequenceItem(content))
        {
            return false;
        }

        int separator;

        if (content[0] is DOUBLE_QUOTE or SINGLE_QUOTE)
        {
            int close = content.AsSpan(1).IndexOf(content[0]) + 1;

            if (close == 0 || !IsSeparator(content: content, index: close + 1))
            {
                return false;
            }

            key = content[1..close];
            separator = close + 1;
        }
        else
        {
            separator = content.IndexOf(": ", StringComparison.Ordinal);

            if (separator < 0 && content[^1] == ':')
            {
                separator = content.Length - 1;
            }

            if (separator <= 0)
            {
                return false;
            }

            key = content[..separator].TrimEnd();
        }

        value = content[(separator + 1)..].Trim();

        return true;
    }

    private static bool IsSeparator(string content, int index)
    {
        return index < content.Length
            && content[index] == ':'
            && (index + 1 == content.Length || content[index + 1] == ' ');
    }

    private static bool IsSequenceItem(string content)
    {
        return content[0] == SEQUENCE_ITEM && (content.Length == 1 || content[1] == ' ');
    }

    private static bool IsEmptyValue(string value)
    {
        return StringComparer.Ordinal.Equals(x: value, y: "{}")
            || StringComparer.Ordinal.Equals(x: value, y: "null")
            || StringComparer.Ordinal.Equals(x: value, y: "~");
    }

    private static string Unquote(string value)
    {
        return value.Length >= 2 && value[0] is DOUBLE_QUOTE or SINGLE_QUOTE && value[^1] == value[0]
            ? value[1..^1]
            : value;
    }

    private static IReadOnlySet<string> NoInputs()
    {
        return NoInputNames;
    }

    [DebuggerDisplay("{Indent}: {Content}")]
    private readonly record struct YamlLine(int Indent, string Content);
}
