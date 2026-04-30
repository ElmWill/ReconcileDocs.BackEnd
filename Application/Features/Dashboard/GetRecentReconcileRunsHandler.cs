using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Dashboard;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Application.Features.Dashboard;

public sealed class GetRecentReconcileRunsHandler : IRequestHandler<GetRecentReconcileRunsQuery, IReadOnlyList<ReconcileRunSummary>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetRecentReconcileRunsHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ReconcileRunSummary>> Handle(GetRecentReconcileRunsQuery request, CancellationToken cancellationToken)
    {
        var take = request.Take <= 0 ? 20 : request.Take;

        return await _dbContext.ReconcileRuns
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(take)
            .Select(run => new ReconcileRunSummary(
                run.Id,
                run.SpreadsheetUploadId,
                run.StatementUploadId,
                run.ParserName,
                run.MatchedCount,
                run.UnmatchedCount,
                run.ErrorCount,
                run.Status,
                run.StartedAtUtc,
                run.CompletedAtUtc))
            .ToListAsync(cancellationToken);
    }
}