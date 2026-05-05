using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Dashboard;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Application.Features.Dashboard;

public sealed class GetDashboardSummaryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryResult>
{
    private readonly IApplicationDbContext _dbContext;

    public GetDashboardSummaryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardSummaryResult> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var uploadedDocuments = await _dbContext.DocumentUploads.CountAsync(cancellationToken);
        var reconcileRuns = await _dbContext.ReconcileRuns.CountAsync(cancellationToken);
        var activeTemplates = await _dbContext.TemplateDefinitions.CountAsync(template => template.IsActive, cancellationToken);
        var successfulRuns = await _dbContext.ReconcileRuns.CountAsync(run => run.Status == 2, cancellationToken);
        var failedRuns = await _dbContext.ReconcileRuns.CountAsync(run => run.Status == 3, cancellationToken);

        return new DashboardSummaryResult(uploadedDocuments, reconcileRuns, activeTemplates, successfulRuns, failedRuns);
    }
}