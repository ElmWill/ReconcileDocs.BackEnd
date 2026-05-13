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
    
    // Track "no" values from latest extraction for post-filtering
    private Dictionary<int, int?>? _lastExtractedNos;

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
        return await ExtractTransactionsWithContextAsync(documentText, null, StatementSourceKind.Pdf, cancellationToken);
    }

    public async Task<IReadOnlyList<ParsedStatementRow>> ExtractTransactionsWithContextAsync(string documentText, MasterServicesContext? masterServices = null, StatementSourceKind sourceKind = StatementSourceKind.Pdf, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return Array.Empty<ParsedStatementRow>();
        }

        // Allow up to 500KB of text for better extraction of large spreadsheets
        var trimmedText = documentText.Length > 500000 ? documentText[..500000] : documentText;

        // Log what we're working with
        _logger.LogInformation("ExtractTransactionsWithContextAsync: sourceKind={SourceKind}, masterServices count={MasterServicesCount}", sourceKind, masterServices?.Services.Count ?? 0);
        List<int> whitelistedNos = new();
        if (masterServices?.Services.Count > 0)
        {
            whitelistedNos = masterServices.Services
                .Where(s => s.Number.HasValue && string.Equals(s.PaymentMethod, "credit card", StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Number!.Value)
                .Distinct()
                .ToList();
            _logger.LogInformation("Credit-card service numbers from master services: {CreditCardNumbers}", string.Join(", ", whitelistedNos));
        }
        
        var prompt = BuildPrompt(trimmedText, masterServices, sourceKind);
        _logger.LogDebug("Full prompt:\n{Prompt}", prompt);

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
            
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return Array.Empty<ParsedStatementRow>();
            }

            _logger.LogDebug("Model response (first 500 chars):\n{Response}", responseText.Substring(0, Math.Min(500, responseText.Length)));
            var parsedRows = ParseModelRowsFromResponse(responseText);
            _logger.LogInformation("Model extracted {RowCount} rows for sourceKind={SourceKind}", parsedRows.Count, sourceKind);
            
            // For spreadsheets, apply post-filtering by page-1 No whitelist
            if (sourceKind == StatementSourceKind.Spreadsheet && whitelistedNos.Count > 0)
            {
                _logger.LogInformation("Applying post-filter: keeping only rows with No in whitelist {WhitelistedNos}", string.Join(", ", whitelistedNos));
                var filteredRows = ApplyWhitelistFilter(parsedRows, whitelistedNos);
                _logger.LogInformation("After whitelist filter: {FilteredRowCount} rows (reduced from {OriginalRowCount})", filteredRows.Count, parsedRows.Count);
                return filteredRows;
            }
            
            return parsedRows;
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

    private static string BuildPrompt(string text, MasterServicesContext? masterServices = null, StatementSourceKind sourceKind = StatementSourceKind.Pdf)
    {
        var masterServicesSection = string.Empty;
        if (masterServices?.Services.Count > 0)
        {
            var creditCardNumbers = masterServices.Services
                .Where(service => service.Number.HasValue && string.Equals(service.PaymentMethod, "credit card", StringComparison.OrdinalIgnoreCase))
                .Select(service => service.Number!.Value)
                .Distinct()
                .ToList();

            if (creditCardNumbers.Count > 0)
            {
                masterServicesSection = "\n\nREFERENCE SERVICES (from master billing list):\nOnly include rows whose No matches one of the credit-card service numbers below. Skip every other row.\n"
                    + "Credit-card service numbers: " + string.Join(", ", creditCardNumbers) + "\n"
                    + "Do not use service names to decide inclusion. Only output rows belonging to the allowed credit-card service numbers.";
                // Logging would happen here in ExtractTransactionsWithContextAsync which has access to ILogger
            }
        }

        var sourceInstructions = sourceKind == StatementSourceKind.Spreadsheet
            ? @"You are extracting rows from a spreadsheet billing sheet.

CRITICAL INSTRUCTIONS:
- IMPORTANT: Extract the row No value from the spreadsheet for each transaction.
- Respond ONLY with valid JSON. Do not include any additional commentary, markdown, code fences, or explanations.

RULES FOR PARSING:
- Analyze only the provided spreadsheet text.
- Preserve the original row order as it appears in the sheet.
- Prefer the service name plus the IDR amount column only.
- If multiple currency columns exist, ignore USD, EUR, dollar, euro, and any non-IDR price columns.
- When extracting from a billing sheet, use Monthly Cost (IDR) first and Yearly Cost (IDR) only as a fallback when Monthly Cost (IDR) is missing or blank.
- Never choose a smaller decimal-style value like 22.2 or 59.14 when an IDR whole-number amount exists in the same row.
- This sheet may be OCR-extracted as separated columns instead of clean rows.
- When the text is column-split, reconstruct each row by following the visible sequence of entries in order.
- Do not merge unrelated descriptions, and do not reassign an amount to a different row.
- If a description is incomplete, keep the exact merchant/reference text that is visible.
- Never guess, merge, or reassign amounts to a different row.
- Ignore headers, footers, legal text, page numbers, summary rows, balance rows, and minimum payment rows.
- IMPORTANT: Exclude/skip any rows where the category or description contains ""Domain"".
- Return an empty array ONLY if genuinely no matching rows are found.
- amount must be positive for charges and negative for refunds or credits.
- Use a dot as the decimal separator and omit currency symbols or thousand separators.

EXPECTED JSON STRUCTURE:
{
    ""transactions"": [
        {
            ""no"": number,
            ""date"": ""YYYY-MM-DD or null"",
            ""description"": ""string"",
            ""amount"": number
        }
    ]
}

Reference note:
The spreadsheet may include separate blocks for row numbers, service names, payment methods, and amounts. Extract the row number (No) from each row."
            : @"You are an AI expert tasked with extracting structured transaction data from credit card statements.

CRITICAL INSTRUCTIONS:
- EXTRACT EVERY SINGLE TRANSACTION without omitting any rows (no sampling, no filtering for ""important"" ones).
- Respond ONLY with valid JSON. Do not include any additional commentary, markdown, code fences, or explanations.

RULES FOR PARSING:
- Analyze only the provided statement text.
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
- IMPORTANT: Exclude/skip any transactions where the category or description contains ""Domain"" (domain payments do not apply to reconciliation).
- Return an empty array ONLY if genuinely no transactions are found.
- amount must be positive for charges and negative for refunds or credits.
- Use a dot as the decimal separator and omit currency symbols or thousand separators.

EXPECTED JSON STRUCTURE:
{
    ""transactions"": [
        {
            ""date"": ""YYYY-MM-DD or null"",
            ""description"": ""string"",
            ""amount"": number
        }
    ]
}

REFERENCE NOTE:
The statement text may include separate blocks for transaction dates, posting dates, descriptions, and amounts. In that case, treat them as one table and keep the row order aligned by position. Ensure you extract ALL data rows, not just a sample.";

        return sourceInstructions + masterServicesSection + "\n\nStatement text:\n" + text;
    }

    private IReadOnlyList<ParsedStatementRow> ParseModelRowsFromResponse(string responseText)
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
        var noValues = new Dictionary<int, int?>(); // Track row index -> No value from JSON

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

            // Filter out Domain payments
            if (description.Contains("Domain", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadAmount(item, out var amount))
            {
                continue;
            }

            var date = TryReadDate(item);
            
            // Try to extract the "no" field if present
            int? noValue = null;
            if (item.TryGetProperty("no", out var noEl) && noEl.ValueKind == JsonValueKind.Number)
            {
                if (noEl.TryGetInt32(out var noInt))
                {
                    noValue = noInt;
                }
            }
            
            rowNumber++;
            rows.Add(new ParsedStatementRow(rowNumber, description, amount, date));
            noValues[rowNumber - 1] = noValue;
        }

        // Store extracted no values for post-filtering
        _lastExtractedNos = noValues;
        _logger.LogDebug("Parsed {RowCount} rows with no values: {NoValues}", rows.Count, string.Join(", ", noValues.Where(kv => kv.Value.HasValue).Select(kv => $"{kv.Key}={kv.Value}")));

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

    private List<ParsedStatementRow> ApplyWhitelistFilter(IReadOnlyList<ParsedStatementRow> rows, List<int> whitelistedNos)
    {
        if (_lastExtractedNos == null || _lastExtractedNos.Count == 0)
        {
            _logger.LogWarning("No extracted 'no' values found in model response, cannot apply whitelist filter. Returning all {RowCount} rows.", rows.Count);
            return new List<ParsedStatementRow>(rows);
        }

        var filtered = new List<ParsedStatementRow>();
        
        for (int i = 0; i < rows.Count; i++)
        {
            if (_lastExtractedNos.TryGetValue(i, out var noValue) && noValue.HasValue && whitelistedNos.Contains(noValue.Value))
            {
                filtered.Add(rows[i]);
                _logger.LogDebug("Keeping row {Index} with No={NoValue}: {Description}", i, noValue.Value, rows[i].Description);
            }
            else
            {
                var extractedNo = _lastExtractedNos.TryGetValue(i, out var val) ? val?.ToString() ?? "null" : "unknown";
                _logger.LogDebug("Filtering out row {Index} with No={NoValue}: {Description}", i, extractedNo, rows[i].Description);
            }
        }
        
        return filtered;
    }

    private sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream, string Format, OllamaOptions Options);
    private sealed record OllamaOptions(int Temperature);
}
