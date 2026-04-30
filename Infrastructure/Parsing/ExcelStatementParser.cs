using ClosedXML.Excel;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class ExcelStatementParser : IStatementParser
{
    public string ParserKey => "excel";

    public bool CanParse(DocumentUpload upload)
    {
        return upload.DocumentKind == (int)Domain.DocumentKind.Spreadsheet || upload.ContentType.Contains("sheet", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ParsedStatementResult> ParseAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(content);
        using var workbook = new XLWorkbook(memoryStream);
        var worksheet = workbook.Worksheets.First();
        var rows = new List<ParsedStatementRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var rowNumber = row.RowNumber();
            var description = row.CellsUsed()
                .Select(cell => cell.GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

            var amount = row.CellsUsed()
                .Select(cell => cell.TryGetValue<decimal>(out var value) ? value : 0m)
                .FirstOrDefault(value => value != 0m);

            rows.Add(new ParsedStatementRow(rowNumber, description, amount, null));
        }

        return Task.FromResult(new ParsedStatementResult(rows));
    }

    public async IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(content);
        using var workbook = new XLWorkbook(memoryStream);
        var worksheet = workbook.Worksheets.First();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = row.RowNumber();
            var description = row.CellsUsed()
                .Select(cell => cell.GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

            var amount = row.CellsUsed()
                .Select(cell => cell.TryGetValue<decimal>(out var value) ? value : 0m)
                .FirstOrDefault(value => value != 0m);

            yield return new ParsedStatementRow(rowNumber, description, amount, null);
        }
    }
}