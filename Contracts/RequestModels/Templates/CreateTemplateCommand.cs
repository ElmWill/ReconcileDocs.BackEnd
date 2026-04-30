using MediatR;
using ReconcileDocs.Domain;
using ReconcileDocs.Contracts.ResponseModels.Templates;

namespace ReconcileDocs.Contracts.RequestModels.Templates;

public sealed record CreateTemplateCommand(
    string Name,
    DocumentKind DocumentKind,
    string ParserKey,
    string ConfigurationJson,
    bool IsActive) : IRequest<CreateTemplateResult>;