using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Contracts.RequestModels.Reconciles;

public sealed record BulkReconcileCommand(
    Guid SpreadsheetUploadId,
    IReadOnlyList<Guid> StatementUploadIds,
    string? Password = null
) : IRequest<BulkReconcileResult>;
