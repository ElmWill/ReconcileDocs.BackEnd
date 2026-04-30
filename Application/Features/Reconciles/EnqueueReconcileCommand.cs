using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed record EnqueueReconcileCommand(Guid SpreadsheetUploadId, Guid StatementUploadId, string? Password = null) : IRequest<StartReconcileResult>;