using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Runs;

namespace Credfeto.Workflow.GitHub.Workflows;

public sealed class WorkflowRunService : IWorkflowRunService
{
    private const string ALLOWLIST_PATH = ".github/cfwf-dispatch-allow";
    private const string RAW_MEDIA_TYPE = "application/vnd.github.raw+json";

    private const char INPUT_SEPARATOR = '=';

    // GitHub caps the whole dispatch payload at 65535 characters
    private const int MAX_INPUT_VALUE_LENGTH = 65535;

    private const string RULE_NO_ALLOWLIST = "the repository has no " + ALLOWLIST_PATH + " on its default branch";
    private const string RULE_NOT_LISTED = "the workflow is not listed in " + ALLOWLIST_PATH;
    private const string RULE_REF = "the ref is not permitted for the workflow by " + ALLOWLIST_PATH;
    private const string RULE_NOT_DISPATCHABLE = "the workflow does not declare workflow_dispatch";

    private readonly ILeakChecker _leakChecker;
    private readonly IGitHubRestClient _restClient;

    public WorkflowRunService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<WorkflowRunResult> RunAsync(
        string repository,
        string workflow,
        string? gitRef,
        IReadOnlyList<string>? inputs,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (!WorkflowFileName.IsValid(workflow))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(workflow),
                rule: "it must be the file name of a workflow, such as build.yml"
            );
        }

        string? validRef = gitRef is null ? null : GitHubNames.Branch(value: gitRef, parameterName: nameof(gitRef));

        Dictionary<string, string> values = [with(StringComparer.Ordinal)];

        if (ReadInputs(inputs: inputs, values: values) is { } problem)
        {
            return ValueTask.FromResult(InvalidInput(workflow: workflow, gitRef: validRef, message: problem));
        }

        return this.RunOrFailAsync(
            request: new(Repository: validRepository, Workflow: workflow, Inputs: values),
            gitRef: validRef,
            cancellationToken: cancellationToken
        );
    }

    // Null when every pair is valid; otherwise the problem, which names the key only once it is known to be safe
    private static string? ReadInputs(IReadOnlyList<string>? inputs, Dictionary<string, string> values)
    {
        if (inputs is null)
        {
            return null;
        }

        foreach (string pair in inputs)
        {
            int separator = pair.IndexOf(INPUT_SEPARATOR, StringComparison.Ordinal);

            if (separator <= 0)
            {
                return "each input must be given as key=value";
            }

            string key = pair[..separator];

            if (!WorkflowInputName.IsValid(key))
            {
                return "an input key is not a valid workflow input name";
            }

            string value = pair[(separator + 1)..];

            if (value.Length > MAX_INPUT_VALUE_LENGTH)
            {
                return $"the input {key} is too long";
            }

            if (!values.TryAdd(key: key, value: value))
            {
                return $"the input {key} is given more than once";
            }
        }

        return null;
    }

    private async ValueTask<WorkflowRunResult> RunOrFailAsync(
        DispatchRequest request,
        string? gitRef,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.DecideAsync(request: request, gitRef: gitRef, cancellationToken: cancellationToken);
        }
        catch (RepositoryNotFoundException exception)
        {
            // The message is fixed text and the validated repository, so it is passed on as the failure
            throw new WorkflowRunFailedException(message: exception.Message, innerException: exception);
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new WorkflowRunFailedException(
                message: $"could not run the workflow {request.Workflow} of {request.Repository}",
                innerException: exception
            );
        }
    }

    // Every rule is checked before anything is dispatched, so a refused call changes nothing
    private async ValueTask<WorkflowRunResult> DecideAsync(
        DispatchRequest request,
        string? gitRef,
        CancellationToken cancellationToken
    )
    {
        string defaultBranch = await this.ReadDefaultBranchAsync(
            repository: request.Repository,
            cancellationToken: cancellationToken
        );
        string targetRef = gitRef ?? defaultBranch;

        string? allowList = await this.ReadTextOrNullAsync(
            relativeUri: $"repos/{request.Repository}/contents/{ALLOWLIST_PATH}?ref={Uri.EscapeDataString(defaultBranch)}",
            cancellationToken: cancellationToken
        );

        if (allowList is null)
        {
            return Refused(workflow: request.Workflow, gitRef: targetRef, rule: RULE_NO_ALLOWLIST);
        }

        if (
            !DispatchAllowList.Parse(allowList).TryGetValue(key: request.Workflow, value: out DispatchAllowEntry? entry)
        )
        {
            return Refused(workflow: request.Workflow, gitRef: targetRef, rule: RULE_NOT_LISTED);
        }

        if (!entry.Permits(gitRef: targetRef, defaultBranch: defaultBranch))
        {
            return Refused(workflow: request.Workflow, gitRef: targetRef, rule: RULE_REF);
        }

        return await this.CheckWorkflowAsync(request: request, gitRef: targetRef, cancellationToken: cancellationToken);
    }

    private async ValueTask<WorkflowRunResult> CheckWorkflowAsync(
        DispatchRequest request,
        string gitRef,
        CancellationToken cancellationToken
    )
    {
        if (!await this.WorkflowExistsAsync(request: request, cancellationToken: cancellationToken))
        {
            return NotFound(
                request: request,
                gitRef: gitRef,
                message: $"the workflow {request.Workflow} was not found in {request.Repository}"
            );
        }

        string? definition = await this.ReadTextOrNullAsync(
            relativeUri: $"repos/{request.Repository}/contents/.github/workflows/{request.Workflow}?ref={Uri.EscapeDataString(gitRef)}",
            cancellationToken: cancellationToken
        );

        if (definition is null)
        {
            return NotFound(
                request: request,
                gitRef: gitRef,
                message: $"the workflow {request.Workflow} was not found on {gitRef}"
            );
        }

        if (WorkflowDispatchDeclaration.TryRead(definition) is not { } declared)
        {
            return Refused(workflow: request.Workflow, gitRef: gitRef, rule: RULE_NOT_DISPATCHABLE);
        }

        if (
            request.Inputs.Keys.Order(StringComparer.Ordinal).FirstOrDefault(key => !declared.Contains(key)) is
            { } unknown
        )
        {
            return InvalidInput(
                workflow: request.Workflow,
                gitRef: gitRef,
                message: $"the workflow does not declare the input {unknown}"
            );
        }

        if (await this.FindLeakAsync(request: request, cancellationToken: cancellationToken) is { } leak)
        {
            string rule = LeakRuleNames.Name(leak.Refusal.Rule);

            return Refused(workflow: request.Workflow, gitRef: gitRef, rule: rule, message: $"input {leak.Key} {rule}");
        }

        return await this.DispatchAsync(request: request, gitRef: gitRef, cancellationToken: cancellationToken);
    }

    private async ValueTask<InputLeak?> FindLeakAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        foreach (
            (string key, string value) in request.Inputs.OrderBy(
                keySelector: pair => pair.Key,
                comparer: StringComparer.Ordinal
            )
        )
        {
            LeakRefusal? refusal = this._leakChecker.CheckValue(subject: LeakSubject.TEXT, value: value)
                ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                    repository: request.Repository,
                    subject: LeakSubject.TEXT,
                    text: value,
                    cancellationToken: cancellationToken
                );

            if (refusal is not null)
            {
                return new(Key: key, Refusal: refusal);
            }
        }

        return null;
    }

    private async ValueTask<string> ReadDefaultBranchAsync(string repository, CancellationToken cancellationToken)
    {
        RepositoryResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }

        return string.IsNullOrEmpty(response.DefaultBranch)
            ? throw new GitHubInvalidResponseException("GitHub returned a repository without a default branch")
            : response.DefaultBranch;
    }

    private async ValueTask<bool> WorkflowExistsAsync(DispatchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}/actions/workflows/{request.Workflow}",
                responseTypeInfo: GitHubJsonContext.Default.WorkflowResponse,
                cancellationToken: cancellationToken
            );

            return true;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async ValueTask<string?> ReadTextOrNullAsync(string relativeUri, CancellationToken cancellationToken)
    {
        try
        {
            return await this._restClient.GetTextAsync(
                relativeUri: relativeUri,
                mediaType: RAW_MEDIA_TYPE,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async ValueTask<WorkflowRunResult> DispatchAsync(
        DispatchRequest request,
        string gitRef,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._restClient.PostAsync(
                relativeUri: $"repos/{request.Repository}/actions/workflows/{request.Workflow}/dispatches",
                body: new WorkflowDispatchRequest(Ref: gitRef, Inputs: request.Inputs),
                requestTypeInfo: GitHubJsonContext.Default.WorkflowDispatchRequest,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return InvalidInput(
                workflow: request.Workflow,
                gitRef: gitRef,
                message: "GitHub refused to run the workflow: " + GitHubHttpFailure.Describe(exception)
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            throw new WorkflowRunFailedException(
                message: $"GitHub refused to run the workflow {request.Workflow} of {request.Repository}: {GitHubHttpFailure.Describe(exception)}",
                innerException: exception
            );
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // Not retried here: the dispatch may have started a run, and a second request would start another
            throw new WorkflowRunFailedException(
                message: $"the request to run the workflow {request.Workflow} of {request.Repository} failed, and may still have reached GitHub",
                innerException: exception
            );
        }

        return await this.DescribeDispatchedAsync(
            request: request,
            gitRef: gitRef,
            cancellationToken: cancellationToken
        );
    }

    // GitHub does not return the new run, so the newest dispatched run on the ref is reported; it can lag the request
    private async ValueTask<WorkflowRunResult> DescribeDispatchedAsync(
        DispatchRequest request,
        string gitRef,
        CancellationToken cancellationToken
    )
    {
        WorkflowRunResponse? newest;

        try
        {
            WorkflowRunsPageResponse page = await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}/actions/workflows/{request.Workflow}/runs?event=workflow_dispatch&branch={Uri.EscapeDataString(gitRef)}&per_page=1",
                responseTypeInfo: GitHubJsonContext.Default.WorkflowRunsPageResponse,
                cancellationToken: cancellationToken
            );

            newest = page.WorkflowRuns?.FirstOrDefault();
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The dispatch itself succeeded, so failing to read the run back is not reported as a failure
            newest = null;
        }

        if (newest?.Id is not { } run)
        {
            return Result(
                outcome: WorkflowRunOutcome.Success,
                message: "dispatched; the run is not visible yet",
                workflow: request.Workflow,
                gitRef: gitRef
            );
        }

        return Result(
            outcome: WorkflowRunOutcome.Success,
            message: $"dispatched; newest run on {gitRef} is {run.ToString(CultureInfo.InvariantCulture)}",
            workflow: request.Workflow,
            gitRef: gitRef
        ) with
        {
            Run = run,
            Url = SafeUrl(newest.HtmlUrl),
        };
    }

    private static Uri? SafeUrl(Uri? url)
    {
        return url is { IsAbsoluteUri: true } && UriSchemes.IsHttps(url) ? url : null;
    }

    private static bool IsUpstreamFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }

    private static WorkflowRunResult Refused(string workflow, string gitRef, string rule)
    {
        return Refused(workflow: workflow, gitRef: gitRef, rule: rule, message: rule);
    }

    private static WorkflowRunResult Refused(string workflow, string gitRef, string rule, string message)
    {
        return Result(
            outcome: WorkflowRunOutcome.Refused,
            message: "workflow run refused: " + message,
            workflow: workflow,
            gitRef: gitRef
        ) with
        {
            Rule = rule,
        };
    }

    private static WorkflowRunResult NotFound(DispatchRequest request, string gitRef, string message)
    {
        return Result(
            outcome: WorkflowRunOutcome.NotFound,
            message: message,
            workflow: request.Workflow,
            gitRef: gitRef
        );
    }

    private static WorkflowRunResult InvalidInput(string workflow, string? gitRef, string message)
    {
        return Result(outcome: WorkflowRunOutcome.InvalidInput, message: message, workflow: workflow, gitRef: gitRef);
    }

    private static WorkflowRunResult Result(WorkflowRunOutcome outcome, string message, string workflow, string? gitRef)
    {
        return new(
            Outcome: outcome,
            Message: message,
            Workflow: workflow,
            Ref: gitRef,
            Run: null,
            Url: null,
            Rule: null
        );
    }

    [DebuggerDisplay("{Repository}: {Workflow}")]
    private sealed record DispatchRequest(
        string Repository,
        string Workflow,
        IReadOnlyDictionary<string, string> Inputs
    );

    [DebuggerDisplay("{Key}: {Refusal}")]
    private sealed record InputLeak(string Key, LeakRefusal Refusal);
}
