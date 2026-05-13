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

public interface IStatementModelExtractor
{
    Task<IReadOnlyList<ParsedStatementRow>> ExtractTransactionsAsync(string documentText, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParsedStatementRow>> ExtractTransactionsWithContextAsync(string documentText, MasterServicesContext? masterServices = null, StatementSourceKind sourceKind = StatementSourceKind.Pdf, CancellationToken cancellationToken = default);
}

public enum StatementSourceKind
{
    Pdf = 1,
    Spreadsheet = 2
}

public sealed record MasterService(int? Number, string ServiceName, string? BillingCycle, DateTime? BillingDate, string? PaymentMethod, string? CcNumber, decimal? CostIdr);

public sealed record MasterServicesContext(IReadOnlyList<MasterService> Services);

public interface IPdfOcrTextExtractor
{
    Task<IReadOnlyList<string>> ExtractPageTextsAsync(string pdfPath, string? password = null, CancellationToken cancellationToken = default);
}

public sealed record PdfParseTrace(int PdfTextLineCount, int OcrTextLineCount, int ModelRowCount);

public sealed record ParsedStatementRow(int RowNumber, string Description, decimal Amount, DateOnly? TransactionDate);

public sealed record ParsedStatementResult(IReadOnlyList<ParsedStatementRow> Rows);