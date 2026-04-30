using System.ComponentModel.DataAnnotations;

namespace ReconcileDocs.Domain.Entities;

public class ParsedDocumentRow
{
    [Key]
    public Guid Id { get; set; }

    public Guid DocumentUploadId { get; set; }

    public int RowNumber { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateOnly? TransactionDate { get; set; }

    [StringLength(100)]
    public string SourceParser { get; set; } = string.Empty;
}