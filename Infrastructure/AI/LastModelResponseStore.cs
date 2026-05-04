using System.Collections.Concurrent;

namespace ReconcileDocs.Infrastructure.AI;

/// <summary>
/// In-memory store for last raw LLM responses for debugging.
/// Note: responses are not persisted and will be cleared on app restart.
/// </summary>
public sealed class LastModelResponseStore : ILastModelResponseStore
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    /// <summary>
    /// Store a raw LLM response keyed by a unique identifier.
    /// </summary>
    public void SetLastResponse(string key, string responseText)
    {
        _store[key] = responseText ?? string.Empty;
    }

    /// <summary>
    /// Retrieve the last stored raw LLM response by key.
    /// </summary>
    public string? GetLastResponse(string key) =>
        _store.TryGetValue(key, out var v) ? v : null;
}
