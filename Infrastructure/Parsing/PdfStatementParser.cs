using UglyToad.PdfPig;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class PdfStatementParser : IStatementParser
{
    public string ParserKey => "pdf";

    public bool CanParse(DocumentUpload upload)
    {
        return upload.DocumentKind == (int)Domain.DocumentKind.StatementPdf || upload.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ParsedStatementResult> ParseAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default)
    {
        // Fallback implementation that collects all rows into memory (kept for compatibility)
        var list = new List<ParsedStatementRow>();
        var enumerator = ParseRowsAsync(upload, content, password, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                list.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return Task.FromResult(new ParsedStatementResult(list));
    }

    public async IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(content);
        var parsingOptions = string.IsNullOrEmpty(password) ? new ParsingOptions() : new ParsingOptions { Password = password };
        using var document = PdfDocument.Open(memoryStream, parsingOptions);

        var rowNumber = 0;
        foreach (var page in document.GetPages())
        {
            var pageLines = page.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in pageLines.Select(text => text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowNumber++;
                yield return new ParsedStatementRow(rowNumber, line, 0m, null);
            }
        }
    }
}