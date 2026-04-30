using MediatR;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Documents;
using ReconcileDocs.Contracts.ResponseModels.Documents;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Documents;

public sealed class UploadDocumentHandler : IRequestHandler<UploadDocumentCommand, UploadDocumentResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public UploadDocumentHandler(IApplicationDbContext dbContext, IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<UploadDocumentResult> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var storageResult = await _fileStorage.SaveAsync(request.FileName, request.Content, cancellationToken);

        var upload = new DocumentUpload
        {
            Id = Guid.NewGuid(),
            DocumentKind = (int)request.DocumentKind,
            OriginalFileName = request.FileName,
            ContentType = request.ContentType,
            StoredFileName = storageResult.StoredFileName,
            StoragePath = storageResult.StoragePath,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Content)),
            SizeBytes = request.Content.LongLength,
            UploadedAtUtc = DateTime.UtcNow,
            ReconcileStatus = 0
        };

        _dbContext.DocumentUploads.Add(upload);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UploadDocumentResult(upload.Id, upload.StoredFileName, upload.StoragePath);
    }
}