using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Services;

public sealed class ReconcileProcessor
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStatementParserResolver _parserResolver;

    public ReconcileProcessor(IApplicationDbContext dbContext, IStatementParserResolver parserResolver)
    {
        _dbContext = dbContext;
        _parserResolver = parserResolver;
    }

    public async Task<StartReconcileResult> ExecuteAsync(ReconcileRun run, Guid spreadsheetUploadId, Guid statementUploadId, string? password, CancellationToken cancellationToken)
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

        var spreadsheetBytes = await File.ReadAllBytesAsync(spreadsheetUpload.StoragePath, cancellationToken);
        var statementBytes = await File.ReadAllBytesAsync(statementUpload.StoragePath, cancellationToken);

        // Parse the spreadsheet fully (assumed smaller) to build lookup
        var parsedSpreadsheet = await spreadsheetParser.ParseAsync(spreadsheetUpload, spreadsheetBytes, password, cancellationToken);

        var spreadsheetRows = parsedSpreadsheet.Rows
            .Select(row => new ParsedDocumentRow
            {
                Id = Guid.NewGuid(),
                DocumentUploadId = spreadsheetUpload.Id,
                RowNumber = row.RowNumber,
                Description = row.Description,
                Amount = row.Amount,
                TransactionDate = row.TransactionDate,
                SourceParser = spreadsheetParser.ParserKey
            })
            .ToList();

        var spreadsheetLookup = parsedSpreadsheet.Rows
            .GroupBy(row => Normalize(row.Description))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First());

        // Stream statement rows and insert in batches
        var statementRowEntities = new List<ParsedDocumentRow>();
        var matchEntities = new List<ReconcileMatch>();
        var matchedCount = 0;
        const int batchSize = 500;

        // mark running
        run.Status = 1;
        run.StartedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await foreach (var statementRow in statementParser.ParseRowsAsync(statementUpload, statementBytes, password, cancellationToken))
        {
            var normalizedDescription = Normalize(statementRow.Description);
            var isMatched = spreadsheetLookup.TryGetValue(normalizedDescription, out var spreadsheetRow);
            if (isMatched)
            {
                matchedCount++;
            }

            var parsedEntity = new ParsedDocumentRow
            {
                Id = Guid.NewGuid(),
                DocumentUploadId = statementUpload.Id,
                RowNumber = statementRow.RowNumber,
                Description = statementRow.Description,
                Amount = statementRow.Amount,
                TransactionDate = statementRow.TransactionDate,
                SourceParser = statementParser.ParserKey
            };

            var matchEntity = new ReconcileMatch
            {
                Id = Guid.NewGuid(),
                ReconcileRunId = run.Id,
                SpreadsheetRowNumber = isMatched ? (spreadsheetRow?.RowNumber ?? 0) : 0,
                StatementRowNumber = statementRow.RowNumber,
                Description = statementRow.Description,
                Amount = statementRow.Amount,
                IsMatched = isMatched
            };

            statementRowEntities.Add(parsedEntity);
            matchEntities.Add(matchEntity);

            if (statementRowEntities.Count >= batchSize)
            {
                _dbContext.ParsedDocumentRows.AddRange(statementRowEntities);
                _dbContext.ReconcileMatches.AddRange(matchEntities);
                await _dbContext.SaveChangesAsync(cancellationToken);
                statementRowEntities.Clear();
                matchEntities.Clear();
            }
        }

        // flush remaining
        if (statementRowEntities.Count > 0)
        {
            _dbContext.ParsedDocumentRows.AddRange(statementRowEntities);
            _dbContext.ReconcileMatches.AddRange(matchEntities);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var unmatchedCount = Math.Max((await _dbContext.ParsedDocumentRows.CountAsync(r => r.DocumentUploadId == statementUpload.Id, cancellationToken)) - matchedCount, 0);

        // Persist spreadsheet rows (if desired to keep together)
        if (spreadsheetRows.Any())
        {
            _dbContext.ParsedDocumentRows.AddRange(spreadsheetRows);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Update run with final counts
        run.MatchedCount = matchedCount;
        run.UnmatchedCount = unmatchedCount;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.Status = 2;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StartReconcileResult(run.Id, run.ParserName, run.MatchedCount, run.UnmatchedCount, run.ErrorCount);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}