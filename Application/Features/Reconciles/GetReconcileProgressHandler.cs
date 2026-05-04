using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.Abstractions;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class GetReconcileProgressHandler : IRequestHandler<GetReconcileProgressQuery, ReconcileProgressResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IReconcileProgressCache _progressCache;

    public GetReconcileProgressHandler(IApplicationDbContext dbContext, IReconcileProgressCache progressCache)
    {
        _dbContext = dbContext;
        _progressCache = progressCache;
    }

    public async Task<ReconcileProgressResult> Handle(GetReconcileProgressQuery request, CancellationToken cancellationToken)
    {
        if (_progressCache.TryGet(request.RunId, out var cached) && cached is not null)
        {
            return cached;
        }

        var run = await _dbContext.ReconcileRuns.FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken);
        if (run is null)
        {
            throw new KeyNotFoundException("Reconcile run not found");
        }

        var result = new ReconcileProgressResult(run.Id, run.Status, run.MatchedCount, run.UnmatchedCount, run.ErrorCount, run.StartedAtUtc, run.CompletedAtUtc, run.ErrorMessage);
        _progressCache.Set(run.Id, result);
        return result;
    }
}