using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed record GetReconcileProgressQuery(Guid RunId) : IRequest<ReconcileProgressResult>;