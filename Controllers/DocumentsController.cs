using MediatR;
using Microsoft.AspNetCore.Mvc;
using ReconcileDocs.Contracts.RequestModels.Documents;
using ReconcileDocs.Contracts.RequestModels.Reconciles;
using ReconcileDocs.Contracts.ResponseModels.Documents;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;
using ReconcileDocs.Domain;
using ReconcileDocs.Application.Features.Reconciles;

namespace ReconcileDocs.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("spreadsheet")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadDocumentResult>> UploadSpreadsheet([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var result = await UploadAsync(file, DocumentKind.Spreadsheet, cancellationToken);
        return Ok(result);
    }

    [HttpPost("statement")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadDocumentResult>> UploadStatement([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var result = await UploadAsync(file, DocumentKind.StatementPdf, cancellationToken);
        return Ok(result);
    }

    [HttpPost("reconcile")]
    public async Task<ActionResult<StartReconcileResult>> Reconcile([FromBody] StartReconcileCommand request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("reconcile-async")]
    public async Task<ActionResult<StartReconcileResult>> ReconcileAsync([FromBody] StartReconcileCommand request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new EnqueueReconcileCommand(request.SpreadsheetUploadId, request.StatementUploadId, request.Password), cancellationToken);
        return Ok(result);
    }

    [HttpGet("reconcile/{id}/progress")]
    public async Task<ActionResult<ReconcileProgressResult>> GetReconcileProgress(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetReconcileProgressQuery(id), cancellationToken);
        return Ok(result);
    }

    private async Task<UploadDocumentResult> UploadAsync(IFormFile file, DocumentKind documentKind, CancellationToken cancellationToken)
    {
        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var command = new UploadDocumentCommand(
            documentKind,
            file.FileName,
            file.ContentType,
            memoryStream.ToArray());

        return await _mediator.Send(command, cancellationToken);
    }
}