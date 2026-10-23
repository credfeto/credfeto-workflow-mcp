using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Cache;

public sealed class RepositoryProjectCache : IRepositoryProjectCache
{
    // Swapped atomically via ImmutableInterlocked so readers never observe a partially updated entry
    private ImmutableDictionary<string, RepositoryCacheEntry> _entries = ImmutableDictionary.Create<
        string,
        RepositoryCacheEntry
    >(StringComparer.OrdinalIgnoreCase);

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
        WorkflowStatusField caseInsensitive = statusField with
        {
            OptionIds = statusField.OptionIds.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        };

        this.Update(repository: repository, update: entry => entry with { StatusField = caseInsensitive });
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
        _ = ImmutableInterlocked.TryRemove(location: ref this._entries, key: repository, value: out _);
    }

    private RepositoryCacheEntry? Find(string repository)
    {
        return this._entries.GetValueOrDefault(repository);
    }

    private void Update(string repository, Func<RepositoryCacheEntry, RepositoryCacheEntry> update)
    {
        _ = ImmutableInterlocked.AddOrUpdate(
            location: ref this._entries,
            key: repository,
            addValueFactory: _ => update(RepositoryCacheEntry.Empty),
            updateValueFactory: (_, existing) => update(existing)
        );
    }
}
