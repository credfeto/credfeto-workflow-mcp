using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal sealed class PullRequestViewProjector
{
    private readonly PullRequestViewNode _node;
    private readonly string _reference;

    public PullRequestViewProjector(string reference, PullRequestViewNode node)
    {
        this._reference = reference;
        this._node = node;
    }

    // Unrequested fields stay null so they are left out of the serialised result
    private static PullRequestView Empty(string reference)
    {
        return new(
            Pr: reference,
            State: null,
            Draft: null,
            Title: null,
            Url: null,
            Base: null,
            Head: null,
            HeadSha: null,
            AutoMerge: null,
            ReviewDecision: null,
            MergeState: null,
            Mergeable: null,
            CreatedAt: null,
            MergedAt: null,
            MergeCommit: null,
            Labels: null,
            Assignees: null,
            Author: null,
            CommitAuthors: null,
            Files: null,
            Body: null
        );
    }

    public PullRequestView Project(IReadOnlyList<string> fields)
    {
        return fields.Aggregate(seed: Empty(this._reference), func: this.Apply);
    }

    private PullRequestView Apply(PullRequestView view, string field)
    {
        return field switch
        {
            PullRequestViewFields.State => view with { State = PullRequestViewText.Clean(this._node.State) },
            PullRequestViewFields.Draft => view with { Draft = this._node.IsDraft is true ? "yes" : "no" },
            PullRequestViewFields.Title => view with { Title = PullRequestViewText.Clean(this._node.Title) },
            PullRequestViewFields.Url => view with { Url = this.Url() },
            PullRequestViewFields.Base => view with { Base = PullRequestViewText.Clean(this._node.BaseRefName) },
            PullRequestViewFields.Head => view with { Head = PullRequestViewText.Clean(this._node.HeadRefName) },
            PullRequestViewFields.HeadSha => view with { HeadSha = PullRequestViewText.Clean(this._node.HeadRefOid) },
            PullRequestViewFields.AutoMerge => view with
            {
                AutoMerge = this._node.AutoMergeRequest is null ? "off" : "on",
            },
            _ => this.ApplyState(view: view, field: field),
        };
    }

    private PullRequestView ApplyState(PullRequestView view, string field)
    {
        bool merged = this._node.Merged is true;

        return field switch
        {
            PullRequestViewFields.ReviewDecision => view with
            {
                ReviewDecision = PullRequestViewText.Clean(this._node.ReviewDecision),
            },
            PullRequestViewFields.MergeState => view with
            {
                MergeState = PullRequestViewText.Clean(this._node.MergeStateStatus),
            },
            PullRequestViewFields.Mergeable => view with
            {
                Mergeable = PullRequestViewText.Clean(this._node.Mergeable),
            },
            PullRequestViewFields.CreatedAt => view with
            {
                CreatedAt = PullRequestViewText.Clean(this._node.CreatedAt),
            },
            PullRequestViewFields.MergedAt => view with
            {
                MergedAt = merged ? PullRequestViewText.Clean(this._node.MergedAt) : string.Empty,
            },
            PullRequestViewFields.MergeCommit => view with
            {
                MergeCommit = merged ? PullRequestViewText.Clean(this._node.MergeCommit?.Oid) : string.Empty,
            },
            _ => this.ApplyPeople(view: view, field: field),
        };
    }

    private PullRequestView ApplyPeople(PullRequestView view, string field)
    {
        return field switch
        {
            PullRequestViewFields.Labels => view with { Labels = this.Join(this._node.Labels, label => label.Name) },
            PullRequestViewFields.Assignees => view with
            {
                Assignees = this.Join(this._node.Assignees, assignee => assignee.Login),
            },
            PullRequestViewFields.Author => view with { Author = PullRequestViewText.Clean(this._node.Author?.Login) },
            PullRequestViewFields.Body => view with { Body = this._node.Body ?? string.Empty },

            // Commit authors and files need further requests, so the service fills them in afterwards
            _ => view,
        };
    }

    // Every pull request has an absolute http(s) url, so anything else means the response was not usable
    private Uri Url()
    {
        if (
            !Uri.TryCreate(uriString: this._node.Url, uriKind: UriKind.Absolute, out Uri? url)
            || !UriSchemes.IsHttp(url)
        )
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        return url;
    }

    private string Join<TNode>(GraphQlConnection<TNode>? connection, Func<TNode, string?> value)
        where TNode : class
    {
        if (connection is not { TotalCount: int total, Nodes: { } nodes } || total > nodes.Count)
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        return string.Join(
            separator: ',',
            values: nodes.OfType<TNode>().Select(value).OfType<string>().Select(PullRequestViewText.Clean)
        );
    }
}
