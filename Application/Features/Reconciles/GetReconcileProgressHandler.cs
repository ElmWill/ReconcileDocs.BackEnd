using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class GetReconcileProgressHandler : IRequestHandler<GetReconcileProgressQuery, ReconcileProgressResult>
{
    private readonly IApplicationDbContext _dbContext;

    public GetReconcileProgressHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReconcileProgressResult> Handle(GetReconcileProgressQuery request, CancellationToken cancellationToken)
    {
        var run = await _dbContext.ReconcileRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken);
        if (run is null)
        {
            throw new KeyNotFoundException("Reconcile run not found");
        }

        return new ReconcileProgressResult(run.Id, run.Status, run.MatchedCount, run.UnmatchedCount, run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage);
    }
}