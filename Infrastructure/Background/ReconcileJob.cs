namespace ReconcileDocs.Infrastructure.Background;

public sealed class ReconcileJob
{
    public Guid RunId { get; set; }
    public Guid SpreadsheetUploadId { get; set; }
    public Guid StatementUploadId { get; set; }
    public string? Password { get; set; }
}