using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Dashboard;
using ReconcileDocs.Contracts.ResponseModels.Dashboard;

namespace ReconcileDocs.Application.Features.Dashboard;

public sealed class GetRecentUploadsHandler : IRequestHandler<GetRecentUploadsQuery, IReadOnlyList<DocumentUploadSummary>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetRecentUploadsHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DocumentUploadSummary>> Handle(GetRecentUploadsQuery request, CancellationToken cancellationToken)
    {
        var take = request.Take <= 0 ? 20 : request.Take;

        return await _dbContext.DocumentUploads
            .OrderByDescending(upload => upload.UploadedAtUtc)
            .Take(take)
            .Select(upload => new DocumentUploadSummary(
                upload.Id,
                upload.DocumentKind,
                upload.OriginalFileName,
                upload.ContentType,
                upload.SizeBytes,
                upload.UploadedAtUtc,
                upload.ReconcileStatus))
            .ToListAsync(cancellationToken);
    }
}