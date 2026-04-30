using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Abstractions;

public interface IStatementParser
{
    bool CanParse(DocumentUpload upload);

    string ParserKey { get; }
    Task<ParsedStatementResult> ParseAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default);
}

public interface IStatementParserResolver
{
    IStatementParser Resolve(DocumentUpload upload, TemplateDefinition? template = null);
}

public sealed record ParsedStatementRow(int RowNumber, string Description, decimal Amount, DateOnly? TransactionDate);

public sealed record ParsedStatementResult(IReadOnlyList<ParsedStatementRow> Rows);