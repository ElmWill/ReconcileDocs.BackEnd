using MediatR;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Templates;
using ReconcileDocs.Contracts.ResponseModels.Templates;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Templates;

public sealed class CreateTemplateHandler : IRequestHandler<CreateTemplateCommand, CreateTemplateResult>
{
    private readonly IApplicationDbContext _dbContext;

    public CreateTemplateHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateTemplateResult> Handle(CreateTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = new TemplateDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DocumentKind = (int)request.DocumentKind,
            ParserKey = request.ParserKey,
            ConfigurationJson = request.ConfigurationJson,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.TemplateDefinitions.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateTemplateResult(template.Id);
    }
}