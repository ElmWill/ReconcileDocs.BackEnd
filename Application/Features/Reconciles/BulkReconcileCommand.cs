using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed record BulkReconcileCommand(
    Guid SpreadsheetUploadId,
    IReadOnlyList<Guid> StatementUploadIds,
    string? Password = null
) : IRequest<BulkReconcileResult>;

public sealed record BulkReconcileResult(
    IReadOnlyList<string> EnqueuedRunIds,
    int TotalEnqueued
);
