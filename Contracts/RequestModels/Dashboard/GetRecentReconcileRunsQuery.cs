using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Contracts.RequestModels.Dashboard;

public sealed record GetRecentReconcileRunsQuery(int Take = 20) : IRequest<IReadOnlyList<ReconcileRunSummary>>;