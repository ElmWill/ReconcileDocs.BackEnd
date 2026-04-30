using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Templates;

namespace ReconcileDocs.Contracts.RequestModels.Templates;

public sealed record SuggestTemplateFromUploadCommand(Guid UploadId) : IRequest<SuggestTemplateFromUploadResult>;
