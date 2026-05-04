using Microsoft.AspNetCore.Mvc;
using ReconcileDocs.Infrastructure.AI;

namespace ReconcileDocs.Controllers;

/// <summary>
/// Diagnostic endpoints for LLM model responses.
/// </summary>
[ApiController]
[Route("api")]
public sealed class LLMController : ControllerBase
{
    private readonly ILastModelResponseStore _store;

    public LLMController(ILastModelResponseStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Get the last raw LLM response from Ollama model extraction.
    /// Useful for debugging why the model returns no transactions.
    /// </summary>
    /// <param name="key">Optional cache key (default: "last")</param>
    /// <returns>Raw JSON response from Ollama</returns>
    [HttpGet("llm-response")]
    public IActionResult GetLastResponse(string? key = "last")
    {
        var cacheKey = key ?? "last";
        var response = _store.GetLastResponse(cacheKey) ?? string.Empty;
        
        // Return as JSON content to prevent HTML escaping
        return Content(response, "application/json; charset=utf-8");
    }
}
