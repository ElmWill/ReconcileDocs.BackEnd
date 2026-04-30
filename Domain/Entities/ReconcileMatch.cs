using System.ComponentModel.DataAnnotations;

namespace ReconcileDocs.Domain.Entities;

public class ReconcileMatch
{
    [Key]
    public Guid Id { get; set; }

    public Guid ReconcileRunId { get; set; }

    public int SpreadsheetRowNumber { get; set; }

    public int StatementRowNumber { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public bool IsMatched { get; set; }
}