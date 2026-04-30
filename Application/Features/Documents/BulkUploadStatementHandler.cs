using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Documents;
using ReconcileDocs.Contracts.ResponseModels.Documents;
using ReconcileDocs.Domain;
using ReconcileDocs.Domain.Entities;
using ReconcileDocs.Application.Services;

namespace ReconcileDocs.Application.Features.Documents;

public sealed class BulkUploadStatementHandler : IRequestHandler<BulkUploadStatementCommand, BulkUploadStatementsResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public BulkUploadStatementHandler(IApplicationDbContext dbContext, IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<BulkUploadStatementsResult> Handle(BulkUploadStatementCommand request, CancellationToken cancellationToken)
    {
        var uploadedFiles = new List<BulkUploadFileInfo>();
        var duplicateHashes = new HashSet<string>();
        var filesByPeriod = new Dictionary<string, List<BulkUploadFileInfo>>();

        // Step 1: Hash all files and check for duplicates in DB
        var fileHashes = new List<(int Index, string FileName, string ContentType, byte[] Content, string Hash)>();
        var existingHashes = await _dbContext.DocumentUploads
            .Select(d => d.StoragePath)
            .ToListAsync(cancellationToken);

        for (int i = 0; i < request.FileContents.Count; i++)
        {
            var hash = FileHashService.ComputeSHA256(request.FileContents[i]);
            fileHashes.Add((i, request.FileNames[i], request.ContentTypes[i], request.FileContents[i], hash));
        }

        // Step 2: Identify duplicates (both within batch and against existing)
        var duplicatesInBatch = new HashSet<string>();
        var hashesToUpload = new HashSet<string>();

        foreach (var (index, fileName, contentType, content, hash) in fileHashes)
        {
            var isDuplicate = duplicatesInBatch.Contains(hash) || existingHashes.Any(p => p.Contains(hash));
            
            if (isDuplicate)
            {
                duplicateHashes.Add(hash);
                duplicatesInBatch.Add(hash);
            }
            else
            {
                hashesToUpload.Add(hash);
            }
        }

        // Step 3: Upload non-duplicate files and extract metadata
        foreach (var (index, fileName, contentType, content, hash) in fileHashes)
        {
            var isDuplicate = duplicateHashes.Contains(hash);
            var period = StatementPeriodExtractor.ExtractPeriod(content, contentType);
            var periodKey = period?.ToString("yyyy-MM") ?? "unknown-period";

            BulkUploadFileInfo fileInfo;

            if (isDuplicate)
            {
                // Don't upload duplicate, mark as such
                fileInfo = new BulkUploadFileInfo(
                    fileName,
                    hash,
                    IsDuplicate: true,
                    DuplicateOf: null,
                    DetectedPeriod: period,
                    DocumentUploadId: Guid.Empty
                );
            }
            else
            {
                // Upload the file
                var upload = new DocumentUpload
                {
                    Id = Guid.NewGuid(),
                    DocumentKind = (int)DocumentKind.StatementPdf,
                    OriginalFileName = fileName,
                    ContentType = contentType,
                    SizeBytes = content.Length,
                    UploadedAtUtc = DateTime.UtcNow,
                    StoragePath = ""
                };

                var storagePath = await _fileStorage.StoreAsync(upload.Id, content, cancellationToken);
                upload.StoragePath = storagePath;

                _dbContext.DocumentUploads.Add(upload);
                await _dbContext.SaveChangesAsync(cancellationToken);

                fileInfo = new BulkUploadFileInfo(
                    fileName,
                    hash,
                    IsDuplicate: false,
                    DuplicateOf: null,
                    DetectedPeriod: period,
                    DocumentUploadId: upload.Id
                );
            }

            uploadedFiles.Add(fileInfo);

            // Group by period (including duplicates for visibility)
            if (!filesByPeriod.ContainsKey(periodKey))
            {
                filesByPeriod[periodKey] = new();
            }
            filesByPeriod[periodKey].Add(fileInfo);
        }

        return new BulkUploadStatementsResult(uploadedFiles, duplicateHashes.ToList(), filesByPeriod);
    }
}
