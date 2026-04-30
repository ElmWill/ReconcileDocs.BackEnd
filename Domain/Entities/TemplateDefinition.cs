using System.ComponentModel.DataAnnotations;

namespace ReconcileDocs.Domain.Entities;

public class TemplateDefinition
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public int DocumentKind { get; set; }

    [StringLength(200)]
    public string ParserKey { get; set; } = string.Empty;

    [StringLength(2000)]
    public string ConfigurationJson { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}