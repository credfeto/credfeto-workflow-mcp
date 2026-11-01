using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Leaks;

public sealed class LeakChecker : ILeakChecker
{
    private static readonly (LeakRule Rule, Func<string, bool> IsRefused)[] LineRules =
    [
        (LeakRule.TOKEN, LeakPatterns.HasToken),
        (LeakRule.HOST_PATH, LeakPatterns.HasHostPath),
        (LeakRule.CONTROL_CHARACTER, LeakPatterns.HasControlCharacter),
        (LeakRule.INVALID_UTF8, LeakPatterns.HasInvalidUtf8),
    ];

    private readonly FrozenSet<string> _privateOwners;
    private readonly IRepositoryVisibilityLookup _visibilityLookup;

    public LeakChecker(IRepositoryVisibilityLookup visibilityLookup, IOptions<WorkflowOptions> options)
    {
        this._visibilityLookup = visibilityLookup;
        this._privateOwners = WorkflowOptionsValidator
            .ParsePrivateOwners(options.Value.PrivateOwners)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public LeakRefusal? CheckBody(string body)
    {
        return CheckText(subject: LeakSubject.BODY, text: body)
            ?? (
                LeakPatterns.IsTooLong(body)
                    ? new(Subject: LeakSubject.BODY, Rule: LeakRule.TOO_LONG, LineNumber: null)
                    : null
            );
    }

    public LeakRefusal? CheckValue(LeakSubject subject, string value)
    {
        return CheckText(subject: subject, text: value);
    }

    public async ValueTask<LeakRefusal?> CheckRepositoryReferencesAsync(
        string repository,
        LeakSubject subject,
        string text,
        CancellationToken cancellationToken
    )
    {
        RepositoryReference[] references =
        [
            .. RepositoryReferenceScanner
                .Scan(text: text, privateOwners: this._privateOwners)
                .Where(reference => !StringComparer.OrdinalIgnoreCase.Equals(x: reference.Repository, y: repository)),
        ];

        // A private or internal target cannot leak a reference to a public audience, and text with no other
        // repository in it needs no lookup at all
        if (
            references.Length == 0
            || !await this.IsPublicTargetAsync(repository: repository, cancellationToken: cancellationToken)
        )
        {
            return null;
        }

        Dictionary<string, bool> isPublic = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (RepositoryReference reference in references)
        {
            if (
                !await this.IsPublicReferenceAsync(
                    reference: reference,
                    isPublic: isPublic,
                    cancellationToken: cancellationToken
                )
            )
            {
                return new(Subject: subject, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: reference.LineNumber);
            }
        }

        return null;
    }

    private async ValueTask<bool> IsPublicTargetAsync(string repository, CancellationToken cancellationToken)
    {
        RepositoryVisibility visibility = await this._visibilityLookup.GetVisibilityAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        return visibility switch
        {
            RepositoryVisibility.NOT_FOUND => throw new RepositoryNotFoundException(
                "the repository being written to was not found, or the caller cannot see it"
            ),
            RepositoryVisibility.PUBLIC => true,
            _ => false,
        };
    }

    private async ValueTask<bool> IsPublicReferenceAsync(
        RepositoryReference reference,
        Dictionary<string, bool> isPublic,
        CancellationToken cancellationToken
    )
    {
        if (this._privateOwners.Contains(reference.Owner))
        {
            return false;
        }

        if (isPublic.TryGetValue(key: reference.Repository, value: out bool cached))
        {
            return cached;
        }

        // A reference that does not resolve may be private and invisible to the caller, so only a public one passes
        RepositoryVisibility visibility = await this._visibilityLookup.GetVisibilityAsync(
            repository: reference.Repository,
            cancellationToken: cancellationToken
        );
        bool result = visibility == RepositoryVisibility.PUBLIC;
        isPublic[reference.Repository] = result;

        return result;
    }

    // The text rules every write applies; a rule that needs more than the text (such as the repository being written
    // to) runs after this pass so the cheap checks refuse first.
    // Each rule scans every line before the next rule runs, so the reported rule is the first in rule order rather
    // than the first problem in the text, matching cfwf.
    public static LeakRefusal? CheckText(LeakSubject subject, string text)
    {
        string[] lines = text.Split('\n');

        foreach ((LeakRule rule, Func<string, bool> isRefused) in LineRules)
        {
            int? lineNumber = FindLine(isRefused: isRefused, lines: lines);

            if (lineNumber is not null)
            {
                return new(Subject: subject, Rule: rule, LineNumber: lineNumber);
            }
        }

        return null;
    }

    private static int? FindLine(Func<string, bool> isRefused, string[] lines)
    {
        int index = Array.FindIndex(array: lines, match: isRefused.Invoke);

        return index < 0 ? null : index + 1;
    }
}
