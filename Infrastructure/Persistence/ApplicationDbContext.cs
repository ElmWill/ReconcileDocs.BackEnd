using ReconcileDocs.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<DocumentUpload> DocumentUploads => Set<DocumentUpload>();

    public DbSet<ReconcileRun> ReconcileRuns => Set<ReconcileRun>();

    public DbSet<TemplateDefinition> TemplateDefinitions => Set<TemplateDefinition>();

    public DbSet<ParsedDocumentRow> ParsedDocumentRows => Set<ParsedDocumentRow>();

    public DbSet<ReconcileMatch> ReconcileMatches => Set<ReconcileMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentUpload>(entity =>
        {
            entity.ToTable("document_uploads");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DocumentKind).HasColumnName("document_kind");
            entity.Property(item => item.OriginalFileName).HasColumnName("original_file_name");
            entity.Property(item => item.ContentType).HasColumnName("content_type");
            entity.Property(item => item.StoredFileName).HasColumnName("stored_file_name");
            entity.Property(item => item.StoragePath).HasColumnName("storage_path");
            entity.Property(item => item.Sha256).HasColumnName("sha256");
            entity.Property(item => item.SizeBytes).HasColumnName("size_bytes");
            entity.Property(item => item.UploadedAtUtc).HasColumnName("uploaded_at_utc");
            entity.Property(item => item.ReconcileStatus).HasColumnName("reconcile_status");
        });

        modelBuilder.Entity<ReconcileRun>(entity =>
        {
            entity.ToTable("reconcile_runs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SpreadsheetUploadId).HasColumnName("spreadsheet_upload_id");
            entity.Property(item => item.StatementUploadId).HasColumnName("statement_upload_id");
            entity.Property(item => item.TemplateDefinitionId).HasColumnName("template_definition_id");
            entity.Property(item => item.ParserName).HasColumnName("parser_name");
            entity.Property(item => item.MatchedCount).HasColumnName("matched_count");
            entity.Property(item => item.UnmatchedCount).HasColumnName("unmatched_count");
            entity.Property(item => item.ErrorCount).HasColumnName("error_count");
            entity.Property(item => item.Status).HasColumnName("status");
            entity.Property(item => item.ErrorMessage).HasColumnName("error_message");
            entity.Property(item => item.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(item => item.CompletedAtUtc).HasColumnName("completed_at_utc");
        });

        modelBuilder.Entity<TemplateDefinition>(entity =>
        {
            entity.ToTable("template_definitions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.DocumentKind).HasColumnName("document_kind");
            entity.Property(item => item.ParserKey).HasColumnName("parser_key");
            entity.Property(item => item.ConfigurationJson).HasColumnName("configuration_json");
            entity.Property(item => item.IsActive).HasColumnName("is_active");
            entity.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<ParsedDocumentRow>(entity =>
        {
            entity.ToTable("parsed_document_rows");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DocumentUploadId).HasColumnName("document_upload_id");
            entity.Property(item => item.RowNumber).HasColumnName("row_number");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.Amount).HasColumnName("amount");
            entity.Property(item => item.TransactionDate).HasColumnName("transaction_date");
            entity.Property(item => item.SourceParser).HasColumnName("source_parser");
        });

        modelBuilder.Entity<ReconcileMatch>(entity =>
        {
            entity.ToTable("reconcile_matches");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ReconcileRunId).HasColumnName("reconcile_run_id");
            entity.Property(item => item.SpreadsheetRowNumber).HasColumnName("spreadsheet_row_number");
            entity.Property(item => item.StatementRowNumber).HasColumnName("statement_row_number");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.Amount).HasColumnName("amount");
            entity.Property(item => item.IsMatched).HasColumnName("is_matched");
        });

        base.OnModelCreating(modelBuilder);
    }
}