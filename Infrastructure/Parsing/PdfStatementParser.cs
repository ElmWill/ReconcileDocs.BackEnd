using UglyToad.PdfPig;
using System.Globalization;
using System.Text.RegularExpressions;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class PdfStatementParser : IStatementParser
{
    private readonly IStatementModelExtractor _modelExtractor;
    private readonly IPdfOcrTextExtractor _ocrTextExtractor;
    private PdfParseTrace? _lastTrace;

    // Matches Indonesian credit card statement format:
    // 15-Mar-26 15-Mar-26 DIGIMAP MALL TAMAN... 002/024 435,375.00
    private static readonly Regex TransactionLineRegex = new(
        "^(?<trx>\\d{1,2}-[A-Za-z]{3}-\\d{2,4}|\\d{1,2}(?:[/-]\\d{1,2}){1,2}|\\d{1,2}\\s+[A-Za-z]{3,4}(?:\\s+\\d{2,4})?)\\s+(?<book>\\d{1,2}-[A-Za-z]{3}-\\d{2,4}|\\d{1,2}(?:[/-]\\d{1,2}){1,2}|\\d{1,2}\\s+[A-Za-z]{3,4}(?:\\s+\\d{2,4})?)\\s+(?<desc>.+?)\\s+(?<amount>[-+]?\\(?\\s*(?:RP\\s*)?[0-9][0-9.,]*\\s*(?:CR|DB)?\\s*\\)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DateTokenRegex = new(
        "\\b\\d{1,2}-[A-Za-z]{3}-\\d{2,4}\\b|\\b\\d{1,2}(?:[/-]\\d{1,2}){1,2}\\b|\\b\\d{1,2}\\s+[A-Za-z]{3,4}(?:\\s+\\d{2,4})?\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AmountAtEndRegex = new(
        "(?<amount>[-+]?\\(?\\s*(?:RP\\s*)?[0-9][0-9.,]*\\s*(?:CR|DB)?\\s*\\)?)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string ParserKey => "pdf";

    public PdfStatementParser(IStatementModelExtractor modelExtractor, IPdfOcrTextExtractor ocrTextExtractor)
    {
        _modelExtractor = modelExtractor;
        _ocrTextExtractor = ocrTextExtractor;
    }

    public bool CanParse(DocumentUpload upload)
    {
        return upload.DocumentKind == (int)Domain.DocumentKind.StatementPdf || upload.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedStatementResult> ParseAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default)
    {
        var list = new List<ParsedStatementRow>();
        await foreach (var row in ParseRowsAsync(upload, content, password, cancellationToken))
        {
            list.Add(row);
        }

        return new ParsedStatementResult(list);
    }

    public async IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(content);
        var parsingOptions = string.IsNullOrEmpty(password) ? new ParsingOptions() : new ParsingOptions { Password = password };
        using var document = PdfDocument.Open(memoryStream, parsingOptions);

        var extractedLines = document.GetPages()
            .SelectMany(page => page.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(text => NormalizeLine(text))
            .Where(text => !string.IsNullOrWhiteSpace(text) && !LooksLikeFooterOrLegalText(text))
            .ToList();
        var ocrLineCount = 0;

        if (LooksLikeSparseFooterOnly(extractedLines))
        {
            var ocrPageTexts = await _ocrTextExtractor.ExtractPageTextsAsync(upload.StoragePath, password, cancellationToken);
            var ocrLines = ocrPageTexts
                .SelectMany(text => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(text => NormalizeLine(text))
                .Where(text => !string.IsNullOrWhiteSpace(text) && !LooksLikeFooterOrLegalText(text))
                .ToList();
            ocrLineCount = ocrLines.Count;

            if (ocrLines.Count > 0)
            {
                extractedLines = ocrLines;
            }
        }

        var modelRows = await _modelExtractor.ExtractTransactionsWithContextAsync(
            string.Join("\n", extractedLines), 
            ExcelStatementParser.GetCurrentMasterServices(), 
            cancellationToken);
        _lastTrace = new PdfParseTrace(extractedLines.Count, ocrLineCount, modelRows.Count);
        if (modelRows.Count > 0)
        {
            var modelRowNumber = 0;
            foreach (var modelRow in modelRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasMeaningfulTransaction(modelRow))
                {
                    continue;
                }

                modelRowNumber++;
                yield return modelRow with { RowNumber = modelRowNumber };
            }

            yield break;
        }

        var fallbackYear = DateTime.UtcNow.Year;
        var rowNumber = 0;
        foreach (var page in document.GetPages())
        {
            var pageLines = page.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in pageLines.Select(text => text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedLine = NormalizeLine(line);
                if (LooksLikeFooterOrLegalText(normalizedLine))
                {
                    continue;
                }

                if (TryExtractReferenceYear(normalizedLine, out var detectedYear))
                {
                    fallbackYear = detectedYear;
                }

                if (!TryParseTransactionLine(normalizedLine, fallbackYear, out var parsedRow))
                {
                    continue;
                }

                rowNumber++;
                yield return parsedRow with { RowNumber = rowNumber };
            }
        }

        // Fallback mode for unsupported layouts: keep only lines that look like meaningful transaction content.
        if (rowNumber == 0)
        {
            foreach (var page in document.GetPages())
            {
                var pageLines = page.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in pageLines.Select(text => text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var normalizedLine = NormalizeLine(line);
                    if (LooksLikeFooterOrLegalText(normalizedLine))
                    {
                        continue;
                    }

                    if (!LooksLikeFallbackTransactionLine(normalizedLine))
                    {
                        continue;
                    }

                    rowNumber++;
                    yield return new ParsedStatementRow(rowNumber, normalizedLine, 0m, null);
                }
            }
        }
    }

    private static bool TryParseTransactionLine(string line, int fallbackYear, out ParsedStatementRow row)
    {
        row = default;

        var match = TransactionLineRegex.Match(line);
        if (!match.Success)
        {
            return TryParseTransactionLineRelaxed(line, fallbackYear, out row);
        }

        var trxToken = match.Groups["trx"].Value;
        var description = match.Groups["desc"].Value.Trim();
        var amountToken = match.Groups["amount"].Value;

        if (!TryParseAmount(amountToken, out var amount))
        {
            return false;
        }

        _ = TryParseDateToken(trxToken, fallbackYear, out var transactionDate);

        row = new ParsedStatementRow(0, description, amount, transactionDate);
        return true;
    }

    private static bool TryParseTransactionLineRelaxed(string line, int fallbackYear, out ParsedStatementRow row)
    {
        row = default;

        var amountMatch = AmountAtEndRegex.Match(line);
        if (!amountMatch.Success)
        {
            return false;
        }

        var amountToken = amountMatch.Groups["amount"].Value;
        if (!TryParseAmount(amountToken, out var amount))
        {
            return false;
        }

        var beforeAmount = line[..amountMatch.Index].Trim();
        if (string.IsNullOrWhiteSpace(beforeAmount))
        {
            return false;
        }

        var dateMatches = DateTokenRegex.Matches(beforeAmount);
        if (dateMatches.Count == 0)
        {
            return false;
        }

        var trxToken = dateMatches[0].Value;
        var descStart = dateMatches.Count >= 2
            ? dateMatches[1].Index + dateMatches[1].Length
            : dateMatches[0].Index + dateMatches[0].Length;

        if (descStart >= beforeAmount.Length)
        {
            return false;
        }

        var description = beforeAmount[descStart..].Trim();
        if (description.Length < 3)
        {
            return false;
        }

        _ = TryParseDateToken(trxToken, fallbackYear, out var transactionDate);
        row = new ParsedStatementRow(0, description, amount, transactionDate);
        return true;
    }

    private static string NormalizeLine(string line)
    {
        var normalized = line.Replace('\u00A0', ' ').Trim();
        normalized = Regex.Replace(normalized, "\\s+", " ");
        return normalized;
    }

    private static bool TryExtractReferenceYear(string line, out int year)
    {
        year = 0;
        var match = Regex.Match(line, "(20[0-9]{2})");
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Value, out year);
    }

    private static bool TryParseDateToken(string token, int fallbackYear, out DateOnly? date)
    {
        date = null;
        var trimmed = token.Trim();

        var formats = new[]
        {
            "dd-MMM-yy", "dd-MMM-yyyy",  // Indonesian format: 15-Mar-26
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd",
            "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
        };

        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fullDate))
        {
            date = DateOnly.FromDateTime(fullDate);
            return true;
        }

        if (DateTime.TryParseExact(trimmed, new[] { "dd/MM", "d/M", "dd-MM", "d-M" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortDate))
        {
            date = new DateOnly(fallbackYear, shortDate.Month, shortDate.Day);
            return true;
        }

        // Support day + month text formats often seen in statements (e.g. "12 APR", "12 MEI").
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[0], out var day) && TryParseMonth(parts[1], out var month))
        {
            var year = fallbackYear;
            if (parts.Length >= 3 && int.TryParse(parts[2], out var parsedYear))
            {
                year = parsedYear < 100 ? 2000 + parsedYear : parsedYear;
            }

            if (day >= 1 && day <= DateTime.DaysInMonth(year, month))
            {
                date = new DateOnly(year, month, day);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMonth(string monthToken, out int month)
    {
        month = 0;
        var key = monthToken.Trim().ToUpperInvariant();
        return key switch
        {
            "JAN" or "JANUARY" => SetMonth(1, out month),
            "FEB" or "FEBRUARY" => SetMonth(2, out month),
            "MAR" or "MARCH" => SetMonth(3, out month),
            "APR" or "APRIL" => SetMonth(4, out month),
            "MEI" or "MAY" => SetMonth(5, out month),
            "JUN" or "JUNE" => SetMonth(6, out month),
            "JUL" or "JULY" => SetMonth(7, out month),
            "AGS" or "AGU" or "AUG" or "AUGUST" => SetMonth(8, out month),
            "SEP" or "SEPT" or "SEPTEMBER" => SetMonth(9, out month),
            "OKT" or "OCT" or "OCTOBER" => SetMonth(10, out month),
            "NOV" or "NOVEMBER" => SetMonth(11, out month),
            "DES" or "DEC" or "DECEMBER" => SetMonth(12, out month),
            _ => false
        };
    }

    private static bool SetMonth(int value, out int month)
    {
        month = value;
        return true;
    }

    private static bool TryParseAmount(string token, out decimal amount)
    {
        amount = 0m;
        var upper = token.ToUpperInvariant();
        var isNegative = upper.Contains("CR") || upper.Contains("(") || upper.StartsWith("-");

        var cleaned = upper
            .Replace("RP", string.Empty, StringComparison.Ordinal)
            .Replace("CR", string.Empty, StringComparison.Ordinal)
            .Replace("DB", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        if (!decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (isNegative && amount > 0m)
        {
            amount = -amount;
        }

        return true;
    }

    private static bool LooksLikeFallbackTransactionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length < 5)
        {
            return false;
        }

        if (!trimmed.Any(char.IsDigit))
        {
            return false;
        }

        var upper = trimmed.ToUpperInvariant();
        
        // Reject page headers and footer keywords
        if (upper.StartsWith("PAGE ") || upper.StartsWith("HALAMAN ") || upper.Contains("TOTAL") || upper.Contains("BALANCE"))
        {
            return false;
        }

        if (LooksLikeFooterOrLegalText(trimmed))
        {
            return false;
        }

        // Reject PDF object syntax and internals
        if (upper.Contains(" OBJ") || upper.Contains("ENDOBJ") || upper.Contains("STREAM") || 
            upper.Contains("<<") || upper.Contains(">>") || upper.StartsWith("/") ||
            upper.Contains("/TYPE") || upper.Contains("/FILTER"))
        {
            return false;
        }

        // Reject lines that are mostly non-ASCII or special characters (likely binary/corrupted)
        var readableChars = trimmed.Count(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == ',' || c == '.');
        var readableRatio = (double)readableChars / trimmed.Length;
        if (readableRatio < 0.6)
        {
            return false;
        }

        // Require at least 2 "words" (space-separated tokens) to avoid junk single tokens
        var wordCount = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 2)
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeFooterOrLegalText(string line)
    {
        var upper = line.ToUpperInvariant();
        return upper.Contains("SYARAT DAN KETENTUAN")
            || upper.Contains("BUKU PETUNJUK LAYANAN")
            || upper.Contains("WWW.MANDIRIKARTUKREDIT.COM")
            || upper.Contains("HAL-HAL YANG PERLU DIKETAHUI")
            || upper.Contains("SALDO TERHUTANG");
    }

    private static bool HasMeaningfulTransaction(ParsedStatementRow row)
    {
        return !string.IsNullOrWhiteSpace(row.Description)
            && !LooksLikeFooterOrLegalText(row.Description)
            && row.Description.Length >= 3;
    }

    private static bool LooksLikeSparseFooterOnly(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return true;
        }

        if (lines.Count <= 8)
        {
            return true;
        }

        var transactionLikeCount = lines.Count(line => line.Any(char.IsDigit) && !LooksLikeFooterOrLegalText(line));
        return transactionLikeCount <= 2;
    }

    public override string ToString()
    {
        if (_lastTrace is null)
        {
            return ParserKey;
        }

        return $"{ParserKey}[pdf:{_lastTrace.PdfTextLineCount},ocr:{_lastTrace.OcrTextLineCount},model:{_lastTrace.ModelRowCount}]";
    }
}