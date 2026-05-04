using ClosedXML.Excel;
using System.Globalization;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class ExcelStatementParser : IStatementParser
{
    private static readonly HashSet<string> DescriptionHeaderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "bill", "description", "keterangan", "merchant", "nama produk", "transaksi"
    };

    private static readonly HashSet<string> AmountHeaderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "jumlah", "nominal", "total", "nilai", "tagihan"
    };

    private static readonly HashSet<string> IgnoredDescriptionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "month", "bulan", "bill", "amount", "rp", "total tagihan", "pembayaran minimum"
    };

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
        var rows = ParseWorksheetRows(worksheet).ToList();

        return Task.FromResult(new ParsedStatementResult(rows));
    }

    public async IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(content);
        using var workbook = new XLWorkbook(memoryStream);
        var worksheet = workbook.Worksheets.First();

        foreach (var parsedRow in ParseWorksheetRows(worksheet))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return parsedRow;
        }
    }

    private static IEnumerable<ParsedStatementRow> ParseWorksheetRows(IXLWorksheet worksheet)
    {
        var usedRows = worksheet.RowsUsed().OrderBy(r => r.RowNumber()).ToList();
        if (usedRows.Count == 0)
        {
            yield break;
        }

        var (headerRowNumber, descriptionColumn, amountColumn) = FindHeader(usedRows);
        var startRowNumber = headerRowNumber.HasValue ? headerRowNumber.Value + 1 : usedRows.First().RowNumber();

        foreach (var row in usedRows.Where(r => r.RowNumber() >= startRowNumber))
        {
            var (description, amount) = ExtractFields(row, descriptionColumn, amountColumn);
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            if (IgnoredDescriptionTokens.Contains(description.Trim()))
            {
                continue;
            }

            // Filter obvious non-transaction lines while keeping valid zero-amount rows possible.
            if (amount == 0m && LooksLikeHeaderRow(description))
            {
                continue;
            }

            yield return new ParsedStatementRow(row.RowNumber(), description, amount, null);
        }
    }

    private static (int? HeaderRowNumber, int? DescriptionColumn, int? AmountColumn) FindHeader(IReadOnlyList<IXLRow> rows)
    {
        foreach (var row in rows.Take(12))
        {
            int? descriptionColumn = null;
            int? amountColumn = null;

            foreach (var cell in row.CellsUsed())
            {
                var value = NormalizeToken(cell.GetString());
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (descriptionColumn is null && DescriptionHeaderTokens.Contains(value))
                {
                    descriptionColumn = cell.Address.ColumnNumber;
                }

                if (amountColumn is null && AmountHeaderTokens.Contains(value))
                {
                    amountColumn = cell.Address.ColumnNumber;
                }
            }

            if (descriptionColumn.HasValue && amountColumn.HasValue)
            {
                return (row.RowNumber(), descriptionColumn, amountColumn);
            }
        }

        return (null, null, null);
    }

    private static (string Description, decimal Amount) ExtractFields(IXLRow row, int? descriptionColumn, int? amountColumn)
    {
        var description = string.Empty;
        if (descriptionColumn.HasValue)
        {
            description = row.Cell(descriptionColumn.Value).GetString().Trim();
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = row.CellsUsed()
                .Select(cell => cell.GetString().Trim())
                .FirstOrDefault(value => IsUsefulDescription(value)) ?? string.Empty;
        }

        decimal amount = 0m;
        if (amountColumn.HasValue)
        {
            if (!TryParseAmountFromCell(row.Cell(amountColumn.Value), out amount))
            {
                // In some sheets, "Rp" and numeric amount are in adjacent columns.
                TryParseAmountFromCell(row.Cell(amountColumn.Value + 1), out amount);
            }
        }

        if (amount == 0m)
        {
            foreach (var cell in row.CellsUsed())
            {
                if (TryParseAmountFromCell(cell, out var parsedAmount) && parsedAmount != 0m)
                {
                    amount = parsedAmount;
                    break;
                }
            }
        }

        return (description, amount);
    }

    private static bool TryParseAmountFromCell(IXLCell cell, out decimal amount)
    {
        amount = 0m;
        if (cell.TryGetValue<decimal>(out var numeric))
        {
            amount = numeric;
            return true;
        }

        var raw = cell.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim().ToUpperInvariant();
        cleaned = cleaned.Replace("RP", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        // Indonesian format: 435.375,00
        if (cleaned.Contains(',') || cleaned.Contains('.'))
        {
            var normalized = cleaned.Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(",", ".", StringComparison.Ordinal);
            if (decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
            {
                return true;
            }
        }

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }

    private static bool IsUsefulDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeToken(value);
        if (IgnoredDescriptionTokens.Contains(normalized))
        {
            return false;
        }

        return !TryParseAmountString(value, out _);
    }

    private static bool TryParseAmountString(string value, out decimal amount)
    {
        var cellLike = value.Trim().ToUpperInvariant()
            .Replace("RP", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        return decimal.TryParse(cellLike, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }

    private static bool LooksLikeHeaderRow(string description)
    {
        var normalized = NormalizeToken(description);
        return DescriptionHeaderTokens.Contains(normalized) || AmountHeaderTokens.Contains(normalized);
    }

    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}