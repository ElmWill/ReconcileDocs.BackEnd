using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Contracts.RequestModels.Dashboard;

public sealed record GetDashboardSummaryQuery() : IRequest<DashboardSummaryResult>;