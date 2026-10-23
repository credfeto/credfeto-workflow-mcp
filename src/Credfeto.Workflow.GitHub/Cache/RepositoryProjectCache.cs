using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.GitHub.Cache;

public sealed class RepositoryProjectCache : IRepositoryProjectCache
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Swapped atomically via ImmutableInterlocked so readers never observe a partially updated entry.
    // Keys are the caller's token hash followed by the repository, so ids fetched with one caller's token are never
    // served to another caller; the hash is fixed-length upper-case hex, so ignoring case only affects the repository.
    private ImmutableDictionary<string, RepositoryCacheEntry> _entries = ImmutableDictionary.Create<
        string,
        RepositoryCacheEntry
    >(StringComparer.OrdinalIgnoreCase);

    public RepositoryProjectCache(IHttpContextAccessor httpContextAccessor)
    {
        this._httpContextAccessor = httpContextAccessor;
    }

    public WorkflowProject? GetProject(string repository)
    {
        return this.Find(repository)?.Project;
    }

    public void SetProject(string repository, WorkflowProject project)
    {
        this.Update(repository: repository, update: entry => entry with { Project = project });
    }

    public WorkflowStatusField? GetStatusField(string repository)
    {
        return this.Find(repository)?.StatusField;
    }

    public void SetStatusField(string repository, WorkflowStatusField statusField)
    {
        // Ordinal keeps option names that differ only by case distinct; WorkflowStatusFieldExtensions.FindOptionId resolves case
        WorkflowStatusField snapshot = statusField with
        {
            OptionIds = statusField.OptionIds.ToFrozenDictionary(StringComparer.Ordinal),
        };

        this.Update(repository: repository, update: entry => entry with { StatusField = snapshot });
    }

    public string? GetItemId(string repository, WorkItemKind kind, int number)
    {
        return this.Find(repository)?.ItemIds.GetValueOrDefault(new WorkItemKey(Kind: kind, Number: number));
    }

    public void SetItemId(string repository, WorkItemKind kind, int number, string itemId)
    {
        WorkItemKey key = new(Kind: kind, Number: number);

        this.Update(
            repository: repository,
            update: entry => entry with { ItemIds = entry.ItemIds.SetItem(key: key, value: itemId) }
        );
    }

    public void Invalidate(string repository)
    {
        _ = ImmutableInterlocked.TryRemove(location: ref this._entries, key: this.CacheKey(repository), value: out _);
    }

    private RepositoryCacheEntry? Find(string repository)
    {
        return this._entries.GetValueOrDefault(this.CacheKey(repository));
    }

    private void Update(string repository, Func<RepositoryCacheEntry, RepositoryCacheEntry> update)
    {
        _ = ImmutableInterlocked.AddOrUpdate(
            location: ref this._entries,
            key: this.CacheKey(repository),
            addValueFactory: _ => update(RepositoryCacheEntry.Empty),
            updateValueFactory: (_, existing) => update(existing)
        );
    }

    private string CacheKey(string repository)
    {
        return this.CallerCacheKey() + repository;
    }

    private string CallerCacheKey()
    {
        // Never fall back to a shared bucket: an anonymous entry would be served to every caller without a token
        return CallerIdentity.Of(this._httpContextAccessor.HttpContext)?.CacheKey
            ?? throw new CallerTokenMissingException();
    }
}
