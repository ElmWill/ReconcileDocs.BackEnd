using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class GetReconcileMatchesHandler : IRequestHandler<GetReconcileMatchesQuery, GetReconcileMatchesResult>
{
    private readonly IApplicationDbContext _dbContext;

    public GetReconcileMatchesHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GetReconcileMatchesResult> Handle(GetReconcileMatchesQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize ?? 50;
        var pageNumber = request.PageNumber ?? 1;
        var skip = (pageNumber - 1) * pageSize;

        var run = await _dbContext.ReconcileRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.RunId, cancellationToken);

        if (run is null)
        {
            return new GetReconcileMatchesResult(Array.Empty<ReconcileMatchDto>(), 0);
        }

        var spreadsheetRows = _dbContext.ParsedDocumentRows
            .AsNoTracking()
            .Where(row => row.DocumentUploadId == run.SpreadsheetUploadId);

        var statementRows = _dbContext.ParsedDocumentRows
            .AsNoTracking()
            .Where(row => row.DocumentUploadId == run.StatementUploadId);

        var query =
            from match in _dbContext.ReconcileMatches.AsNoTracking().Where(m => m.ReconcileRunId == request.RunId)
            join spreadsheetRow in spreadsheetRows on match.SpreadsheetRowNumber equals spreadsheetRow.RowNumber into spreadsheetJoin
            from spreadsheetRow in spreadsheetJoin.DefaultIfEmpty()
            join statementRow in statementRows on match.StatementRowNumber equals statementRow.RowNumber into statementJoin
            from statementRow in statementJoin.DefaultIfEmpty()
            select new
            {
                Match = match,
                SpreadsheetRow = spreadsheetRow,
                StatementRow = statementRow
            };

        if (request.MatchedOnly.HasValue)
        {
            query = query.Where(item => item.Match.IsMatched == request.MatchedOnly.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var matches = await query
            .OrderByDescending(item => item.Match.IsMatched)
            .ThenBy(item => item.Match.StatementRowNumber)
            .Skip(skip)
            .Take(pageSize)
            .Select(item => new ReconcileMatchDto(
                item.Match.Id,
                item.Match.SpreadsheetRowNumber,
                item.Match.StatementRowNumber,
                item.Match.Description,
                item.Match.Amount,
                item.Match.IsMatched,
                item.SpreadsheetRow != null ? item.SpreadsheetRow.Description : null,
                item.SpreadsheetRow != null ? item.SpreadsheetRow.Amount : null,
                item.SpreadsheetRow != null ? item.SpreadsheetRow.TransactionDate : null,
                item.StatementRow != null ? item.StatementRow.Description : item.Match.Description,
                item.StatementRow != null ? item.StatementRow.Amount : item.Match.Amount,
                item.StatementRow != null ? item.StatementRow.TransactionDate : null))
            .ToListAsync(cancellationToken);

        return new GetReconcileMatchesResult(matches, total);
    }
}
