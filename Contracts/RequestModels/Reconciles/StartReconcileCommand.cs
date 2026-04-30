using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Contracts.RequestModels.Reconciles;

public sealed record StartReconcileCommand(Guid SpreadsheetUploadId, Guid StatementUploadId, string? Password = null) : IRequest<StartReconcileResult>;