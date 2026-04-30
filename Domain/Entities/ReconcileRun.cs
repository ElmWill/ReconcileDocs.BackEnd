using System.ComponentModel.DataAnnotations;

namespace ReconcileDocs.Domain.Entities;

public class ReconcileRun
{
    [Key]
    public Guid Id { get; set; }

    public Guid SpreadsheetUploadId { get; set; }

    public Guid StatementUploadId { get; set; }

    public Guid? TemplateDefinitionId { get; set; }

    [StringLength(100)]
    public string ParserName { get; set; } = string.Empty;

    public int MatchedCount { get; set; }

    public int UnmatchedCount { get; set; }

    public int ErrorCount { get; set; }

    public int Status { get; set; }

    [StringLength(500)]
    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}