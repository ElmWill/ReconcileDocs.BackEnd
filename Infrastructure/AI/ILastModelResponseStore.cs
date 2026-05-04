namespace ReconcileDocs.Infrastructure.AI;

/// <summary>
/// Stores the last raw LLM response for diagnostic purposes.
/// </summary>
public interface ILastModelResponseStore
{
    /// <summary>
    /// Store a raw LLM response keyed by a unique identifier.
    /// </summary>
    void SetLastResponse(string key, string responseText);

    /// <summary>
    /// Retrieve the last stored raw LLM response by key.
    /// </summary>
    string? GetLastResponse(string key);
}
