using MediatR;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Reconciles;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class BulkReconcileHandler : IRequestHandler<BulkReconcileCommand, BulkReconcileResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IBackgroundTaskQueue _queue;

    public BulkReconcileHandler(IApplicationDbContext dbContext, IBackgroundTaskQueue queue)
    {
        _dbContext = dbContext;
        _queue = queue;
    }

    public async Task<BulkReconcileResult> Handle(BulkReconcileCommand request, CancellationToken cancellationToken)
    {
        var enqueuedRunIds = new List<string>();

        foreach (var statementUploadId in request.StatementUploadIds)
        {
            var run = new ReconcileRun
            {
                Id = Guid.NewGuid(),
                SpreadsheetUploadId = request.SpreadsheetUploadId,
                StatementUploadId = statementUploadId,
                ParserName = string.Empty,
                MatchedCount = 0,
                UnmatchedCount = 0,
                ErrorCount = 0,
                Status = 0, // queued
                StartedAtUtc = DateTime.UtcNow
            };

            _dbContext.ReconcileRuns.Add(run);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Enqueue the job
            await _queue.EnqueueAsync(run.Id, request.SpreadsheetUploadId, statementUploadId, request.Password);
            enqueuedRunIds.Add(run.Id.ToString());
        }

        return new BulkReconcileResult(enqueuedRunIds, enqueuedRunIds.Count);
    }
}
