using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<DocumentUpload> DocumentUploads { get; }

    DbSet<ReconcileRun> ReconcileRuns { get; }

    DbSet<TemplateDefinition> TemplateDefinitions { get; }

    DbSet<ParsedDocumentRow> ParsedDocumentRows { get; }

    DbSet<ReconcileMatch> ReconcileMatches { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}