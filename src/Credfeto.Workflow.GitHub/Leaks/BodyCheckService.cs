using System;
using System.Buffers;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Leaks;

public sealed class BodyCheckService : IBodyCheckService
{
    private const string EMPTY_BODY = "the body is empty";

    // cfwf's first emptiness check is a C-locale [:space:] byte test, so only ASCII whitespace counts as blank before
    // the leak check; a body of Unicode blanks is only called empty after it
    private static readonly SearchValues<char> AsciiWhitespace = SearchValues.Create(" \t\n\v\f\r");

    private static readonly BodyCheckResult Passed = new(
        Outcome: BodyCheckOutcome.Passed,
        Rule: null,
        Line: null,
        Message: null
    );

    private readonly ILeakChecker _leakChecker;

    public BodyCheckService(ILeakChecker leakChecker)
    {
        this._leakChecker = leakChecker;
    }

    public ValueTask<BodyCheckResult> CheckAsync(string repository, string body, CancellationToken cancellationToken)
    {
        // Both arguments are validated before the visibility lookups the private repository rule may make
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (!body.AsSpan().ContainsAnyExcept(AsciiWhitespace))
        {
            throw EmptyBody();
        }

        return this.CheckValidAsync(repository: validRepository, body: body, cancellationToken: cancellationToken);
    }

    private async ValueTask<BodyCheckResult> CheckValidAsync(
        string repository,
        string body,
        CancellationToken cancellationToken
    )
    {
        LeakRefusal? refusal =
            this._leakChecker.CheckBody(body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: repository,
                subject: LeakSubject.BODY,
                text: body,
                cancellationToken: cancellationToken
            );

        if (refusal is not null)
        {
            return Refused(refusal);
        }

        // Runs after the leak check, as in cfwf, so a body that holds a control character is refused for it rather
        // than called empty
        return TitleValidator.HasVisibleText(body) ? Passed : throw EmptyBody();
    }

    // Only the rule and the line are reported, never the matched text
    private static BodyCheckResult Refused(LeakRefusal refusal)
    {
        string rule = LeakRuleNames.Name(refusal.Rule);
        string line = refusal.LineNumber is { } number
            ? $" (line {number.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return new(
            Outcome: BodyCheckOutcome.Refused,
            Rule: rule,
            Line: refusal.LineNumber,
            Message: $"body refused: {rule}{line}"
        );
    }

    private static ParameterValidationException EmptyBody()
    {
        return new(EMPTY_BODY) { ParameterName = "body", Rule = EMPTY_BODY };
    }
}
