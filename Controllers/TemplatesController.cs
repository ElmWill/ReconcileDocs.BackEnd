using MediatR;
using Microsoft.AspNetCore.Mvc;
using ReconcileDocs.Contracts.RequestModels.Templates;
using ReconcileDocs.Contracts.ResponseModels.Templates;
using ReconcileDocs.Domain;

namespace ReconcileDocs.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TemplatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult<CreateTemplateResult>> Create([FromBody] CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateTemplateCommand(
            request.Name,
            request.DocumentKind,
            request.ParserKey,
            request.ConfigurationJson,
            request.IsActive);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("suggest")]
    public async Task<ActionResult<SuggestTemplateFromUploadResult>> SuggestFromUpload([FromBody] SuggestTemplateFromUploadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var command = new SuggestTemplateFromUploadCommand(request.UploadId);
            var result = await _mediator.Send(command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { title = "Upload not found." });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { title = "Uploaded file is missing from storage." });
        }
    }
}

public sealed record CreateTemplateRequest(string Name, DocumentKind DocumentKind, string ParserKey, string ConfigurationJson, bool IsActive);
public sealed record SuggestTemplateFromUploadRequest(Guid UploadId);