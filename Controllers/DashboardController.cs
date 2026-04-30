using MediatR;
using Microsoft.AspNetCore.Mvc;
using ReconcileDocs.Contracts.RequestModels.Dashboard;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResult>> Summary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDashboardSummaryQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("uploads")]
    public async Task<ActionResult<IReadOnlyList<DocumentUploadSummary>>> Uploads([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetRecentUploadsQuery(take), cancellationToken);
        return Ok(result);
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<ReconcileRunSummary>>> Runs([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetRecentReconcileRunsQuery(take), cancellationToken);
        return Ok(result);
    }
}