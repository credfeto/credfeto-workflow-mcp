using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Branches;

public sealed class RequiredChecksReader : IRequiredChecksReader
{
    private const string ENFORCEMENT_OFF = "off";

    private const string REQUIRED_STATUS_CHECKS_RULE = "required_status_checks";

    // GitHub answers the rulesets endpoint with this 403 on a private repository whose plan has no rulesets, which
    // means no ruleset can require a check rather than that the caller lacks access
    private const string RULESETS_UNAVAILABLE_MESSAGE = "Upgrade to GitHub Pro or make this repository public";

    // GitHub reports -1 for a check that any source may satisfy
    private const long ANY_SOURCE_APP_ID = -1;

    private readonly IGitHubRestClient _restClient;

    public RequiredChecksReader(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<BranchInfo> ReadBranchAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validBranch = GitHubNames.Branch(value: branch, parameterName: nameof(branch));

        return ReadOrFailAsync(
            repository: validRepository,
            branch: validBranch,
            read: () =>
                this.ReadBranchInfoAsync(
                    repository: validRepository,
                    branch: validBranch,
                    cancellationToken: cancellationToken
                )
        );
    }

    public ValueTask<IReadOnlyList<RequiredCheck>> ReadRequiredChecksAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validBranch = GitHubNames.Branch(value: branch, parameterName: nameof(branch));

        return ReadOrFailAsync(
            repository: validRepository,
            branch: validBranch,
            read: () =>
                this.ReadAllRequiredChecksAsync(
                    repository: validRepository,
                    branch: validBranch,
                    cancellationToken: cancellationToken
                )
        );
    }

    public async ValueTask<BranchRequiredChecksResponse> ReadRequiredCheckNamesAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<RequiredCheck> checks = await this.ReadRequiredChecksAsync(
            repository: repository,
            branch: branch,
            cancellationToken: cancellationToken
        );

        // The arguments were validated by the read above, so they are returned exactly as the caller gave them
        return new(
            Repo: repository,
            Branch: branch,
            Checks:
            [
                .. checks
                    .Select(check => UpstreamText.ReplaceLineBreaking(check.Name))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ]
        );
    }

    private static async ValueTask<T> ReadOrFailAsync<T>(string repository, string branch, Func<ValueTask<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RequiredChecksUnreadableException(
                message: $"could not read the required checks of {repository} branch {branch}",
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<RequiredCheck>> ReadAllRequiredChecksAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        // The branch is read first so a missing branch or repository is reported before the rulesets are asked for
        BranchInfo branchInfo = await this.ReadBranchInfoAsync(
            repository: repository,
            branch: branch,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<RequiredCheck> rulesetChecks = await this.ReadRulesetChecksAsync(
            repository: repository,
            branch: branch,
            cancellationToken: cancellationToken
        );

        return [.. branchInfo.Checks.Concat(rulesetChecks).Distinct()];
    }

    private async ValueTask<BranchInfo> ReadBranchInfoAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        BranchResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/branches/{GitHubPath.BranchPath(branch)}",
                responseTypeInfo: GitHubJsonContext.Default.BranchResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw await this.MissingBranchOrRepositoryAsync(
                repository: repository,
                branch: branch,
                branchNotFound: exception,
                cancellationToken: cancellationToken
            );
        }

        string sha = response.Commit?.Sha is { Length: > 0 } value
            ? value
            : throw new GitHubInvalidResponseException("GitHub returned a branch without a head commit");

        return new(Sha: sha, Checks: ProtectionChecks(response.Protection?.RequiredStatusChecks));
    }

    // A missing branch and a missing or hidden repository both answer 404, so the repository is read to tell them apart
    private async ValueTask<Exception> MissingBranchOrRepositoryAsync(
        string repository,
        string branch,
        HttpRequestException branchNotFound,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }

        return new BranchNotFoundException(
            message: $"the branch {branch} was not found in {repository}",
            innerException: branchNotFound
        );
    }

    private static IReadOnlyList<RequiredCheck> ProtectionChecks(RequiredStatusChecks? required)
    {
        if (required is null || StringComparer.Ordinal.Equals(x: required.EnforcementLevel, y: ENFORCEMENT_OFF))
        {
            return [];
        }

        IReadOnlyList<RequiredCheck> checks =
        [
            .. (required.Checks ?? []).Select(check =>
                check is null
                    ? throw new GitHubInvalidResponseException("GitHub returned an empty required status check")
                    : new RequiredCheck(Name: ValidName(check.Context), AppId: ValidAppId(check.AppId))
            ),
        ];

        // Older protection lists only the legacy context names; a name already listed with its app adds nothing
        HashSet<string> listed = new(collection: checks.Select(check => check.Name), comparer: StringComparer.Ordinal);
        IEnumerable<RequiredCheck> legacy = (required.Contexts ?? [])
            .Select(ValidName)
            .Where(name => !listed.Contains(name))
            .Select(name => new RequiredCheck(Name: name, AppId: null));

        return [.. checks.Concat(legacy)];
    }

    private async ValueTask<IReadOnlyList<RequiredCheck>> ReadRulesetChecksAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<BranchRuleResponse> rules;

        try
        {
            rules = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{repository}/rules/branches/{GitHubPath.BranchPath(branch)}?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.BranchRuleResponseArray,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (IsRulesetsUnavailable(exception))
        {
            return [];
        }

        return
        [
            .. rules
                .OfType<BranchRuleResponse>()
                .Where(rule => StringComparer.Ordinal.Equals(x: rule.Type, y: REQUIRED_STATUS_CHECKS_RULE))
                .SelectMany(rule =>
                    rule.Parameters?.RequiredStatusChecks
                    ?? throw new GitHubInvalidResponseException(
                        "GitHub returned a required status checks rule without its checks"
                    )
                )
                .Select(check =>
                    check is null
                        ? throw new GitHubInvalidResponseException("GitHub returned an empty required status check")
                        : new RequiredCheck(Name: ValidName(check.Context), AppId: ValidAppId(check.IntegrationId))
                ),
        ];
    }

    private static bool IsRulesetsUnavailable(HttpRequestException exception)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden
            && GitHubHttpFailure.UpstreamMessage(exception) is { } message
            && message.Contains(value: RULESETS_UNAVAILABLE_MESSAGE, comparisonType: StringComparison.Ordinal);
    }

    private static string ValidName(string? name)
    {
        return string.IsNullOrEmpty(name)
            ? throw new GitHubInvalidResponseException("GitHub returned a required status check without a name")
            : name;
    }

    private static long? ValidAppId(long? appId)
    {
        return appId switch
        {
            null or ANY_SOURCE_APP_ID => null,
            < 0 => throw new GitHubInvalidResponseException(
                "GitHub returned a required status check with an invalid app id"
            ),
            _ => appId,
        };
    }
}
