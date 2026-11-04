using System;
using System.Buffers;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Leaks;

namespace Credfeto.Workflow.GitHub.Search;

public static partial class IssueSearchText
{
    public const int MAX_WORDS_LENGTH = 256;

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;
    private const string SUBJECT = "text";

    private static readonly SearchValues<char> LettersAndDigits = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"
    );

    // Returns the search words that are safe to place in a query; usage errors and leak refusals name the rule and
    // never the value, because the caller can see the error and the text may be hostile or sensitive
    public static string Words(string text, string parameterName)
    {
        ThrowIf(
            invalid: text.AsSpan().ContainsAnyExceptInRange(lowInclusive: ' ', highInclusive: '~'),
            parameterName: parameterName,
            rule: "it must contain only printable ASCII characters"
        );

        ThrowIf(
            invalid: !text.AsSpan().ContainsAny(LettersAndDigits),
            parameterName: parameterName,
            rule: "it must contain a letter or digit"
        );

        string words = Sanitise(text);

        ThrowIf(
            invalid: Operator().IsMatch(words),
            parameterName: parameterName,
            rule: "it must not contain the search operators AND, OR or NOT"
        );

        ThrowIf(invalid: words.Length > MAX_WORDS_LENGTH, parameterName: parameterName, rule: "it is too long");

        // Sanitising can both hide a token shape and form one, so the token rule sees the text and the words
        ThrowIfRefused(refused: LeakPatterns.HasToken(text) || LeakPatterns.HasToken(words), rule: "token");
        ThrowIfRefused(refused: LeakPatterns.HasHostPath(text), rule: "host path");

        return words;
    }

    // Every character that could form search syntax (qualifier colons, phrase quotes, grouping brackets) becomes a
    // space, so the words can never scope the search to another repository
    public static string Sanitise(string text)
    {
        return string.Join(
            separator: ' ',
            value: NotWordCharacter()
                .Replace(input: text, replacement: " ")
                .Split(separator: ' ', options: StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static void ThrowIf(bool invalid, string parameterName, string rule)
    {
        if (invalid)
        {
            throw ParameterValidationException.For(parameterName: parameterName, rule: rule);
        }
    }

    private static void ThrowIfRefused(bool refused, string rule)
    {
        if (refused)
        {
            throw new IssueSearchRefusedException($"{SUBJECT} refused: {rule}") { Subject = SUBJECT, Rule = rule };
        }
    }

    [GeneratedRegex(
        pattern: "[^A-Za-z0-9 _.#'-]",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex NotWordCharacter();

    // Only whole upper-case words are operators to GitHub search; lower or mixed case are plain words
    [GeneratedRegex(
        pattern: @"(^| )-?(AND|OR|NOT)( |\z)",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Operator();
}
