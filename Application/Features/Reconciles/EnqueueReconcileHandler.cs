using MediatR;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class EnqueueReconcileHandler : IRequestHandler<EnqueueReconcileCommand, Contracts.ResponseModels.Reconciles.StartReconcileResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IBackgroundTaskQueue _queue;

    public EnqueueReconcileHandler(IApplicationDbContext dbContext, IBackgroundTaskQueue queue)
    {
        _dbContext = dbContext;
        _queue = queue;
    }

    public async Task<Contracts.ResponseModels.Reconciles.StartReconcileResult> Handle(EnqueueReconcileCommand request, CancellationToken cancellationToken)
    {
        var run = new ReconcileRun
        {
            Id = Guid.NewGuid(),
            SpreadsheetUploadId = request.SpreadsheetUploadId,
            StatementUploadId = request.StatementUploadId,
            ParserName = string.Empty,
            MatchedCount = 0,
            UnmatchedCount = 0,
            ErrorCount = 0,
            Status = 0, // queued
            StartedAtUtc = DateTime.UtcNow
        };

        _dbContext.ReconcileRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueAsync(run.Id, request.SpreadsheetUploadId, request.StatementUploadId, request.Password);

        return new Contracts.ResponseModels.Reconciles.StartReconcileResult(run.Id, run.ParserName, run.MatchedCount, run.UnmatchedCount, run.ErrorCount);
    }
}