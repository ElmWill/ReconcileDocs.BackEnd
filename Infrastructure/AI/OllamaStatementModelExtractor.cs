using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReconcileDocs.Application.Abstractions;

namespace ReconcileDocs.Infrastructure.AI;

public sealed class OllamaStatementModelExtractor : IStatementModelExtractor
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaStatementModelExtractor> _logger;
    private readonly ILastModelResponseStore _lastResponseStore;

    public OllamaStatementModelExtractor(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaStatementModelExtractor> logger, ILastModelResponseStore lastResponseStore)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _lastResponseStore = lastResponseStore;

        var baseUrl = _configuration["Ollama:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:11434";
        }

        _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<IReadOnlyList<ParsedStatementRow>> ExtractTransactionsAsync(string documentText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return Array.Empty<ParsedStatementRow>();
        }

        var trimmedText = documentText.Length > 30000 ? documentText[..30000] : documentText;
        _logger.LogDebug("Ollama: full input text length={TextLength}\n--- BEGIN INPUT ---\n{Text}\n--- END INPUT ---",
            documentText.Length, documentText);
        if (trimmedText.Length < documentText.Length)
        {
            _logger.LogDebug("Ollama: input was truncated to {TrimmedLength} characters for the model request", trimmedText.Length);
        }
        
        var prompt = BuildPrompt(trimmedText);

        try
        {
            var request = new OllamaGenerateRequest(
                GetModelName(),
                prompt,
                false,
                "json",
                new OllamaOptions(0));

            using var response = await _httpClient.PostAsJsonAsync("/api/generate", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama extraction failed with HTTP {StatusCode}", response.StatusCode);
                return Array.Empty<ParsedStatementRow>();
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Store the raw response for diagnostic endpoint
            _lastResponseStore.SetLastResponse("last", responseText);
            _logger.LogDebug("Ollama raw response: {ResponseText}", responseText);
            
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return Array.Empty<ParsedStatementRow>();
            }

            return ParseModelRowsFromResponse(responseText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama extraction failed, parser will use fallback logic");
            return Array.Empty<ParsedStatementRow>();
        }
    }

    private string GetModelName()
    {
        return _configuration["Ollama:Model"] ?? "gemma4:e4b";
    }

    private static string BuildPrompt(string text)
    {
                return """
You are an AI expert tasked with extracting structured transaction data from the attached credit card statement.

RULES:
- Analyze only the provided statement text.
- Respond ONLY with valid JSON. Do not include any additional commentary, markdown, code fences, or explanations.
- Preserve the original transaction order as it appears in the document.
- This statement may be OCR-extracted as separated columns instead of clean rows.
- When the text is column-split, reconstruct each transaction by following the visible sequence of entries in order, not by trying to infer relationships from nearby words.
- Do not merge unrelated descriptions, and do not reassign an amount to a different merchant.
- If multiple description lines appear before the amount column, keep the merchant text that belongs to the same sequential row.
- If a date is missing or unclear, return null.
- If a description is incomplete, keep the exact merchant/reference text that is visible.
- Never guess, merge, or reassign amounts to a different row.
- Never move an amount from one transaction to another, even if lines appear split.
- Ignore headers, footers, legal text, page numbers, summary rows, balance rows, and minimum payment rows.
- Return an empty array if no transactions are found.
- amount must be positive for charges and negative for refunds or credits.
- Use a dot as the decimal separator and omit currency symbols or thousand separators.

EXPECTED JSON STRUCTURE:
{
    "transactions": [
        {
            "date": "YYYY-MM-DD or null",
            "description": "string",
            "amount": number
        }
    ]
}

REFERENCE NOTE:
The statement text may include separate blocks for transaction dates, posting dates, descriptions, and amounts. In that case, treat them as one table and keep the row order aligned by position.

Statement text:
""" + text;
    }

    private static IReadOnlyList<ParsedStatementRow> ParseModelRowsFromResponse(string responseText)
    {
        var cleanedJson = ExtractJsonPayload(responseText);
        using var doc = JsonDocument.Parse(cleanedJson);

        JsonElement transactions;
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("transactions", out var txArray))
        {
            transactions = txArray;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            transactions = doc.RootElement;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("response", out var responseEl) && responseEl.ValueKind == JsonValueKind.String)
        {
            var nested = responseEl.GetString();
            return string.IsNullOrWhiteSpace(nested)
                ? Array.Empty<ParsedStatementRow>()
                : ParseModelRowsFromResponse(nested);
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.Object && messageEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
        {
            var nested = contentEl.GetString();
            return string.IsNullOrWhiteSpace(nested)
                ? Array.Empty<ParsedStatementRow>()
                : ParseModelRowsFromResponse(nested);
        }
        else
        {
            return Array.Empty<ParsedStatementRow>();
        }

        var rows = new List<ParsedStatementRow>();
        var rowNumber = 0;

        foreach (var item in transactions.EnumerateArray())
        {
            if (!item.TryGetProperty("description", out var descEl))
            {
                continue;
            }

            var description = (descEl.GetString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            if (!TryReadAmount(item, out var amount))
            {
                continue;
            }

            var date = TryReadDate(item);
            rowNumber++;
            rows.Add(new ParsedStatementRow(rowNumber, description, amount, date));
        }

        return rows;
    }

    private static string ExtractJsonPayload(string responseText)
    {
        var trimmed = responseText.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimStart('`');
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..].Trim();
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("response", out var responseEl) && responseEl.ValueKind == JsonValueKind.String)
                {
                    var nested = responseEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return ExtractJsonPayload(nested);
                    }
                }

                if (doc.RootElement.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.Object && messageEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var nested = contentEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return ExtractJsonPayload(nested);
                    }
                }

                if (doc.RootElement.TryGetProperty("transactions", out _))
                {
                    return trimmed;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to bracket extraction
        }

        var firstObject = trimmed.IndexOf('{');
        var firstArray = trimmed.IndexOf('[');

        if (firstObject < 0 && firstArray < 0)
        {
            throw new JsonException("Ollama response did not contain JSON content.");
        }

        if (firstArray >= 0 && (firstObject < 0 || firstArray < firstObject))
        {
            var lastArray = trimmed.LastIndexOf(']');
            if (lastArray > firstArray)
            {
                return trimmed.Substring(firstArray, lastArray - firstArray + 1);
            }
        }

        var lastObject = trimmed.LastIndexOf('}');
        if (lastObject > firstObject)
        {
            return trimmed.Substring(firstObject, lastObject - firstObject + 1);
        }

        return trimmed;
    }

    private static bool TryReadAmount(JsonElement item, out decimal amount)
    {
        amount = 0m;
        if (!item.TryGetProperty("amount", out var amountEl))
        {
            return false;
        }

        if (amountEl.ValueKind == JsonValueKind.Number)
        {
            return amountEl.TryGetDecimal(out amount);
        }

        if (amountEl.ValueKind == JsonValueKind.String)
        {
            var raw = amountEl.GetString() ?? string.Empty;
            var cleaned = raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
            return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
        }

        return false;
    }

    private static DateOnly? TryReadDate(JsonElement item)
    {
        if (!item.TryGetProperty("date", out var dateEl))
        {
            return null;
        }

        if (dateEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = dateEl.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    private sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream, string Format, OllamaOptions Options);
    private sealed record OllamaOptions(int Temperature);
}
