using MediatR;
using ReconcileDocs.Domain;
using ReconcileDocs.Contracts.ResponseModels.Documents;

namespace ReconcileDocs.Contracts.RequestModels.Documents;

public sealed record UploadDocumentCommand(
    DocumentKind DocumentKind,
    string FileName,
    string ContentType,
    byte[] Content) : IRequest<UploadDocumentResult>;