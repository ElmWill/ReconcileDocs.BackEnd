using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.Abstractions;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;
using ReconcileDocs.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ReconcileDocs.Application.Services;

public sealed class ReconcileProcessor
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStatementParserResolver _parserResolver;
    private readonly IReconcileProgressCache _progressCache;
    private readonly ILogger<ReconcileProcessor> _logger;

    public ReconcileProcessor(IApplicationDbContext dbContext, IStatementParserResolver parserResolver, IReconcileProgressCache progressCache, ILogger<ReconcileProcessor> logger)
    {
        _dbContext = dbContext;
        _parserResolver = parserResolver;
        _progressCache = progressCache;
        _logger = logger;
    }

    public async Task<StartReconcileResult> ExecuteAsync(ReconcileRun run, Guid spreadsheetUploadId, Guid statementUploadId, string? password, CancellationToken cancellationToken)
    {
        try
        {
            var spreadsheetUpload = await _dbContext.DocumentUploads.FirstAsync(upload => upload.Id == spreadsheetUploadId, cancellationToken);
            var statementUpload = await _dbContext.DocumentUploads.FirstAsync(upload => upload.Id == statementUploadId, cancellationToken);

            var spreadsheetTemplate = await _dbContext.TemplateDefinitions
                .Where(template => template.IsActive && template.DocumentKind == spreadsheetUpload.DocumentKind)
                .OrderByDescending(template => template.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var statementTemplate = await _dbContext.TemplateDefinitions
                .Where(template => template.IsActive && template.DocumentKind == statementUpload.DocumentKind)
                .OrderByDescending(template => template.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var spreadsheetParser = _parserResolver.Resolve(spreadsheetUpload, spreadsheetTemplate);
            var statementParser = _parserResolver.Resolve(statementUpload, statementTemplate);

            run.ParserName = $"{SanitizeForDb(spreadsheetParser.ParserKey, 50)}/{SanitizeForDb(statementParser.ParserKey, 50)}";
            run.Status = 1;
            run.ErrorMessage = null;
            run.StartedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _progressCache.Set(run.Id, new ReconcileProgressResult(run.Id, run.Status, run.MatchedCount, run.UnmatchedCount, run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));

            var spreadsheetBytes = await File.ReadAllBytesAsync(spreadsheetUpload.StoragePath, cancellationToken);
            var statementBytes = await File.ReadAllBytesAsync(statementUpload.StoragePath, cancellationToken);

            // Parse the spreadsheet fully (assumed smaller) to build lookup
            var parsedSpreadsheet = await spreadsheetParser.ParseAsync(spreadsheetUpload, spreadsheetBytes, password, cancellationToken);
            var parsedStatement = await statementParser.ParseAsync(statementUpload, statementBytes, password, cancellationToken);

            run.ParserName = $"{SanitizeForDb(spreadsheetParser.ParserKey, 50)}/{SanitizeForDb(statementParser.ParserKey, 50)}";
            await _dbContext.SaveChangesAsync(cancellationToken);

            var spreadsheetRows = parsedSpreadsheet.Rows
                .Where(row => HasMeaningfulData(row.Description, row.Amount, row.TransactionDate))
                .Select(row => new ParsedDocumentRow
                {
                    Id = Guid.NewGuid(),
                    DocumentUploadId = spreadsheetUpload.Id,
                    RowNumber = row.RowNumber,
                    Description = SanitizeForDb(row.Description, 500),
                    Amount = row.Amount,
                    TransactionDate = row.TransactionDate,
                    SourceParser = SanitizeForDb(spreadsheetParser.ParserKey, 100)
                })
                .ToList();

            var spreadsheetRowsByDescription = parsedSpreadsheet.Rows
                .Where(row => HasMeaningfulData(row.Description, row.Amount, row.TransactionDate))
                .GroupBy(row => Normalize(row.Description))
                .ToDictionary(group => group.Key, group => group.ToList());

            // Log all available spreadsheet amounts for debugging
            var availableAmounts = parsedSpreadsheet.Rows
                .Where(row => HasMeaningfulData(row.Description, row.Amount, row.TransactionDate))
                .Select(row => row.Amount)
                .Distinct()
                .OrderBy(a => a)
                .ToList();
            _logger.LogDebug("Reconcile: Spreadsheet loaded with {SpreadsheetRowCount} rows, {UniqueAmounts} unique amounts: {Amounts}", 
                parsedSpreadsheet.Rows.Count, availableAmounts.Count, string.Join(", ", availableAmounts));

            // Stream statement rows and insert in batches
            var statementRowEntities = new List<ParsedDocumentRow>();
            var matchEntities = new List<ReconcileMatch>();
            var matchedCount = 0;
            var processedCount = 0;
            var usedSpreadsheetRowNumbers = new HashSet<int>();
            const int batchSize = 500;

            foreach (var statementRow in parsedStatement.Rows)
            {
                if (!HasMeaningfulData(statementRow.Description, statementRow.Amount, statementRow.TransactionDate))
                {
                    continue;
                }

                var normalizedDescription = Normalize(statementRow.Description);
                var isMatched = false;
                ParsedStatementRow? spreadsheetRow = null;

                // Try exact match first
                if (spreadsheetRowsByDescription.TryGetValue(normalizedDescription, out var candidateRows))
                {
                    var exactCandidates = candidateRows
                        .Where(candidate => !usedSpreadsheetRowNumbers.Contains(candidate.RowNumber) && candidate.Amount == statementRow.Amount)
                        .ToList();
                    
                    spreadsheetRow = exactCandidates.FirstOrDefault(candidate =>
                        DatesMatchStrict(candidate.TransactionDate, statementRow.TransactionDate));

                    if (spreadsheetRow is not null)
                    {
                        isMatched = true;
                        usedSpreadsheetRowNumbers.Add(spreadsheetRow.RowNumber);
                        _logger.LogDebug("Reconcile: exact match found for statement row '{Description}' {Amount}", statementRow.Description, statementRow.Amount);
                    }
                    else if (exactCandidates.Any())
                    {
                        _logger.LogDebug("Reconcile: exact description match but no amount/date match. Statement: {Description} {Amount}. Candidates: {CandidateCount}", 
                            statementRow.Description, statementRow.Amount, exactCandidates.Count);
                    }
                }
                else
                {
                    _logger.LogDebug("Reconcile: no exact description match for '{Description}'. Normalized: '{NormalizedDescription}'. Available keys count: {KeyCount}",
                        statementRow.Description, normalizedDescription, spreadsheetRowsByDescription.Count);
                }

                // Fallback 1: fuzzy match if exact description not found
                if (!isMatched && spreadsheetRowsByDescription.Count > 0)
                {
                    var allCandidates = spreadsheetRowsByDescription.Values.SelectMany(rows => rows).ToList();
                    var amountMatches = allCandidates
                        .Where(candidate => !usedSpreadsheetRowNumbers.Contains(candidate.RowNumber) && candidate.Amount == statementRow.Amount)
                        .ToList();

                    if (!amountMatches.Any())
                    {
                        _logger.LogDebug("Reconcile: no amount match for {Amount}. Available amounts: {AvailableAmounts}",
                            statementRow.Amount, string.Join(", ", allCandidates.Select(c => c.Amount).Distinct().OrderBy(a => a)));
                    }

                    var fuzzyCandidate = amountMatches
                        .FirstOrDefault(candidate =>
                            DatesMatchTolerant(candidate.TransactionDate, statementRow.TransactionDate) &&
                            DescriptionsAreSimilar(Normalize(candidate.Description), normalizedDescription));

                    if (fuzzyCandidate is not null)
                    {
                        spreadsheetRow = fuzzyCandidate;
                        isMatched = true;
                        usedSpreadsheetRowNumbers.Add(fuzzyCandidate.RowNumber);
                        _logger.LogDebug("Reconcile: fuzzy match found for statement row '{Description}' {Amount} matched to '{CandidateDescription}'", statementRow.Description, statementRow.Amount, fuzzyCandidate.Description);
                    }
                    else if (amountMatches.Any())
                    {
                        var candidateDescriptions = string.Join(" | ", amountMatches.Select(c => c.Description));
                        _logger.LogDebug("Reconcile: amount matched but fuzzy/date failed. Statement: '{Description}' (normalized: '{NormalizedDescription}'). Spreadsheet candidates: {CandidateDescriptions}",
                            statementRow.Description, normalizedDescription, candidateDescriptions);
                    }
                }

                // Fallback 2: match by amount + any partial description overlap (very lenient)
                if (!isMatched)
                {
                    var allCandidates = spreadsheetRowsByDescription.Values.SelectMany(rows => rows).ToList();
                    var amountMatches = allCandidates
                        .Where(candidate => !usedSpreadsheetRowNumbers.Contains(candidate.RowNumber) && candidate.Amount == statementRow.Amount)
                        .ToList();

                    var amountOnlyCandidate = amountMatches
                        .FirstOrDefault(candidate =>
                            DatesMatchTolerant(candidate.TransactionDate, statementRow.TransactionDate) &&
                            HasPartialDescriptionOverlap(Normalize(candidate.Description), normalizedDescription));

                    if (amountOnlyCandidate is not null)
                    {
                        spreadsheetRow = amountOnlyCandidate;
                        isMatched = true;
                        usedSpreadsheetRowNumbers.Add(amountOnlyCandidate.RowNumber);
                        _logger.LogDebug("Reconcile: amount+partial match found for statement row '{Description}' {Amount} matched to '{CandidateDescription}'", statementRow.Description, statementRow.Amount, amountOnlyCandidate.Description);
                    }
                    else if (amountMatches.Any())
                    {
                        _logger.LogDebug("Reconcile: amount matched but partial overlap failed. Statement: '{Description}' {Amount}",
                            statementRow.Description, statementRow.Amount);
                    }
                }

                if (!isMatched)
                {
                    _logger.LogDebug("Reconcile: no match found for statement row '{Description}' {Amount}", statementRow.Description, statementRow.Amount);
                }

                if (isMatched)
                {
                    matchedCount++;
                }

                var parsedEntity = new ParsedDocumentRow
                {
                    Id = Guid.NewGuid(),
                    DocumentUploadId = statementUpload.Id,
                    RowNumber = statementRow.RowNumber,
                    Description = SanitizeForDb(statementRow.Description, 500),
                    Amount = statementRow.Amount,
                    TransactionDate = statementRow.TransactionDate,
                    SourceParser = SanitizeForDb(statementParser.ParserKey, 100)
                };

                var matchEntity = new ReconcileMatch
                {
                    Id = Guid.NewGuid(),
                    ReconcileRunId = run.Id,
                    SpreadsheetRowNumber = isMatched ? (spreadsheetRow?.RowNumber ?? 0) : 0,
                    StatementRowNumber = statementRow.RowNumber,
                    Description = SanitizeForDb(statementRow.Description, 500),
                    Amount = statementRow.Amount,
                    IsMatched = isMatched
                };

                statementRowEntities.Add(parsedEntity);
                matchEntities.Add(matchEntity);

                if (statementRowEntities.Count >= batchSize)
                {
                    var flushCount = statementRowEntities.Count;
                    _dbContext.ParsedDocumentRows.AddRange(statementRowEntities);
                    _dbContext.ReconcileMatches.AddRange(matchEntities);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    processedCount += flushCount;
                    _progressCache.Set(run.Id, new ReconcileProgressResult(run.Id, run.Status, matchedCount, Math.Max(processedCount - matchedCount, 0), run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));
                    statementRowEntities.Clear();
                    matchEntities.Clear();
                }
            }

            // flush remaining
            if (statementRowEntities.Count > 0)
            {
                var flushCount = statementRowEntities.Count;
                _dbContext.ParsedDocumentRows.AddRange(statementRowEntities);
                _dbContext.ReconcileMatches.AddRange(matchEntities);
                await _dbContext.SaveChangesAsync(cancellationToken);
                processedCount += flushCount;
                _progressCache.Set(run.Id, new ReconcileProgressResult(run.Id, run.Status, matchedCount, Math.Max(processedCount - matchedCount, 0), run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));
            }

            var unmatchedCount = Math.Max(processedCount - matchedCount, 0);

            // Persist spreadsheet rows (if desired to keep together)
            if (spreadsheetRows.Any())
            {
                _dbContext.ParsedDocumentRows.AddRange(spreadsheetRows);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Add ReconcileMatch entries for master services that are monthly and missing from the statement
            try
            {
                MasterServicesContext? masterServices = null;
                try
                {
                    var parserType = spreadsheetParser.GetType();
                    var method = parserType.GetMethod("ExtractMasterServicesFromBytes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        var result = method.Invoke(null, new object[] { spreadsheetBytes });
                        masterServices = result as MasterServicesContext;
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogDebug(rex, "Reflection call to ExtractMasterServicesFromBytes failed");
                    masterServices = null;
                }

                if (masterServices?.Services?.Count > 0)
                {
                    var existingSpreadsheetDescriptions = parsedSpreadsheet.Rows
                        .Where(r => HasMeaningfulData(r.Description, r.Amount, r.TransactionDate))
                        .Select(r => Normalize(r.Description))
                        .ToHashSet();

                    var missingMatches = new List<ReconcileMatch>();
                    foreach (var svc in masterServices.Services)
                    {
                        if (string.IsNullOrWhiteSpace(svc.ServiceName)) continue;
                        if (!string.Equals(svc.PaymentMethod, "credit card", StringComparison.OrdinalIgnoreCase)) continue;
                        var billingCycle = svc.BillingCycle ?? string.Empty;
                        if (!billingCycle.Contains("monthly", StringComparison.OrdinalIgnoreCase)) continue;

                        // If any parsed spreadsheet row contains the service name, consider it present
                        var svcNameNorm = Normalize(svc.ServiceName);
                        var present = existingSpreadsheetDescriptions.Any(d => d.Contains(svcNameNorm));
                        if (present) continue;

                        var matchEntity = new ReconcileMatch
                        {
                            Id = Guid.NewGuid(),
                            ReconcileRunId = run.Id,
                            SpreadsheetRowNumber = svc.Number ?? 0,
                            StatementRowNumber = 0,
                            Description = SanitizeForDb(svc.ServiceName, 500),
                            Amount = svc.CostIdr ?? 0m,
                            IsMatched = false
                        };

                        missingMatches.Add(matchEntity);
                    }

                    if (missingMatches.Any())
                    {
                        _dbContext.ReconcileMatches.AddRange(missingMatches);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        run.UnmatchedCount += missingMatches.Count;
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add missing monthly master-service matches");
            }

            // Update run with final counts
            run.MatchedCount = matchedCount;
            run.UnmatchedCount = unmatchedCount;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Status = 2;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _progressCache.Set(run.Id, new ReconcileProgressResult(run.Id, run.Status, run.MatchedCount, run.UnmatchedCount, run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));

            return new StartReconcileResult(run.Id, run.ParserName, run.MatchedCount, run.UnmatchedCount, run.ErrorCount);
        }
        catch (Exception ex)
        {
            run.ErrorMessage = SanitizeForDb(ex.Message, 500);
            run.ErrorCount = Math.Max(run.ErrorCount, 1);
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Status = 3;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _progressCache.Set(run.Id, new ReconcileProgressResult(run.Id, run.Status, run.MatchedCount, run.UnmatchedCount, run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage));
            throw;
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string SanitizeForDb(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // Remove NUL and other non-printable control characters except common whitespace
        var cleaned = new string(value.Where(c => c == '\n' || c == '\r' || c == '\t' || c >= ' ').ToArray());
        // Trim and truncate to maxLength
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned.Substring(0, maxLength);
        }
        return cleaned;
    }

    private static bool HasMeaningfulData(string? description, decimal amount, DateOnly? transactionDate)
    {
        return !string.IsNullOrWhiteSpace(description)
            || amount != 0m
            || transactionDate.HasValue;
    }

    private static bool DatesMatchStrict(DateOnly? left, DateOnly? right)
    {
        if (left is null || right is null)
        {
            return true;
        }

        return left.Value == right.Value;
    }

    private static bool DatesMatchTolerant(DateOnly? left, DateOnly? right)
    {
        // If either is null, consider it a match (missing date)
        if (left is null || right is null)
        {
            return true;
        }

        // If both present, allow within 3 days (handles end-of-cycle variations)
        var daysDiff = Math.Abs((left.Value.DayNumber - right.Value.DayNumber));
        return daysDiff <= 3;
    }

    private static bool DescriptionsAreSimilar(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        // Token-based similarity: split by spaces and check overlap
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return false;
        }

        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();

        // Require at least 40% token overlap (Jaccard similarity) - lowered for OCR tolerance
        var similarity = (double)intersection / union;
        return similarity >= 0.4;
    }

    private static bool HasPartialDescriptionOverlap(string left, string right)
    {
        // Very lenient: just need at least one token in common and minimum length
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        // Filter out very short tokens (noise like "a", "an")
        leftTokens = new HashSet<string>(leftTokens.Where(t => t.Length > 2));
        rightTokens = new HashSet<string>(rightTokens.Where(t => t.Length > 2));

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return false;
        }

        // Just need at least one meaningful token in common
        return leftTokens.Intersect(rightTokens).Count() > 0;
    }
}