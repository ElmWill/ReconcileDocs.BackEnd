using System.ComponentModel.DataAnnotations;

namespace ReconcileDocs.Domain.Entities;

public class DocumentUpload
{
    [Key]
    public Guid Id { get; set; }

    public int DocumentKind { get; set; }

    [StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    [StringLength(260)]
    public string StoredFileName { get; set; } = string.Empty;

    [StringLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    [StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public int ReconcileStatus { get; set; }
}