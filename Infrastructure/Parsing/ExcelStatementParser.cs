using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class ExcelStatementParser : IStatementParser
{
    private readonly IStatementModelExtractor _modelExtractor;
    private readonly ILogger<ExcelStatementParser> _logger;

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

    public ExcelStatementParser(IStatementModelExtractor modelExtractor, ILogger<ExcelStatementParser> logger)
    {
        _modelExtractor = modelExtractor;
        _logger = logger;
    }

    public string ParserKey => "excel";

    public bool CanParse(DocumentUpload upload)
    {
        return upload.DocumentKind == (int)Domain.DocumentKind.Spreadsheet || upload.ContentType.Contains("sheet", StringComparison.OrdinalIgnoreCase);
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
        using var workbook = new XLWorkbook(memoryStream);
        
        // Extract master services context from "List active biling" sheet for use in PDF reconciliation
        var masterServices = ExtractMasterServices(workbook);
        
        // Store in a thread-local or context variable so PdfStatementParser can access it
        // For now, we'll store it in a static field that gets cleared after use
        CurrentMasterServicesContext = masterServices;
        
        try
        {
            var worksheet = SelectWorksheet(workbook);
            var parsedRows = ParseIdrRowsFromWorksheet(worksheet);

            // Prefer deterministic IDR parsing; fallback to LLM only when header mapping fails.
            if (parsedRows.Count == 0)
            {
                var worksheetText = ExtractWorksheetText(worksheet);
                if (!string.IsNullOrWhiteSpace(worksheetText))
                {
                    _logger.LogInformation("Excel deterministic parsing returned no rows for worksheet {WorksheetName}. Falling back to model extraction.", worksheet.Name);
                    parsedRows = (await _modelExtractor.ExtractTransactionsAsync(worksheetText, cancellationToken)).ToList();
                }
            }

            var rowNumber = 0;
            foreach (var row in parsedRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowNumber++;
                yield return row with { RowNumber = rowNumber };
            }
        }
        finally
        {
            CurrentMasterServicesContext = null;
        }
    }

    // Thread-local context for passing master services to PDF parser
    [ThreadStatic]
    private static MasterServicesContext? CurrentMasterServicesContext;

    public static MasterServicesContext? GetCurrentMasterServices()
    {
        return CurrentMasterServicesContext;
    }

    private static MasterServicesContext? ExtractMasterServices(XLWorkbook workbook)
    {
        var listSheet = workbook.Worksheets.FirstOrDefault(ws => 
            string.Equals(ws.Name?.Trim(), "List active biling", StringComparison.OrdinalIgnoreCase));
        
        if (listSheet is null)
        {
            return null;
        }

        var services = new List<MasterService>();
        var usedRows = listSheet.RowsUsed().ToList();
        
        if (usedRows.Count == 0)
        {
            return null;
        }

        // Find header row
        var headerRow = usedRows.FirstOrDefault(row => 
            row.CellsUsed().Any(cell => NormalizeToken(cell.GetString()).Contains("service name")));
        
        if (headerRow is null)
        {
            return null;
        }

        // Map column indices
        var serviceNameCol = 0;
        var billingCycleCol = 0;
        var billingDateCol = 0;
        var paymentMethodCol = 0;
        var ccNumberCol = 0;
        var monthlyCostCol = 0;

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = NormalizeToken(cell.GetString());
            if (string.IsNullOrWhiteSpace(header)) continue;

            if (serviceNameCol == 0 && header.Contains("service name"))
                serviceNameCol = cell.Address.ColumnNumber;
            else if (billingCycleCol == 0 && header.Contains("billing cycle"))
                billingCycleCol = cell.Address.ColumnNumber;
            else if (billingDateCol == 0 && header.Contains("start date"))
                billingDateCol = cell.Address.ColumnNumber;
            else if (paymentMethodCol == 0 && header.Contains("payment method") || header.Contains("account"))
                paymentMethodCol = cell.Address.ColumnNumber;
            else if (ccNumberCol == 0 && (header.Contains("cc number") || header.Contains("account")))
                ccNumberCol = cell.Address.ColumnNumber;
            else if (monthlyCostCol == 0 && header.Contains("monthly") && header.Contains("idr"))
                monthlyCostCol = cell.Address.ColumnNumber;
        }

        // Extract data rows (credit card payments only)
        var dataRows = usedRows.Where(r => r.RowNumber() > headerRow.RowNumber());
        foreach (var row in dataRows)
        {
            var serviceName = serviceNameCol > 0 ? row.Cell(serviceNameCol).GetString().Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Contains("Domain", StringComparison.OrdinalIgnoreCase))
                continue;

            var paymentMethod = paymentMethodCol > 0 ? row.Cell(paymentMethodCol).GetString().Trim() : string.Empty;
            if (!paymentMethod.Equals("credit card", StringComparison.OrdinalIgnoreCase))
                continue;

            var billingCycle = billingCycleCol > 0 ? row.Cell(billingCycleCol).GetString().Trim() : null;
            var billingDateStr = billingDateCol > 0 ? row.Cell(billingDateCol).GetString().Trim() : null;
            var ccNumber = ccNumberCol > 0 ? row.Cell(ccNumberCol).GetString().Trim() : null;
            
            DateTime? billingDate = null;
            if (!string.IsNullOrWhiteSpace(billingDateStr) && DateTime.TryParse(billingDateStr, out var parsedDate))
            {
                billingDate = parsedDate;
            }

            decimal? costIdr = null;
            if (monthlyCostCol > 0)
            {
                var costStr = row.Cell(monthlyCostCol).GetFormattedString().Trim();
                if (TryParseIdrAmount(costStr, out var amount))
                {
                    costIdr = amount;
                }
            }

            services.Add(new MasterService(serviceName, billingCycle, billingDate, paymentMethod, ccNumber, costIdr));
        }

        return services.Count > 0 ? new MasterServicesContext(services.AsReadOnly()) : null;
    }

    private static List<ParsedStatementRow> ParseIdrRowsFromWorksheet(IXLWorksheet worksheet)
    {
        var rows = new List<ParsedStatementRow>();
        var usedRows = worksheet.RowsUsed().ToList();
        if (usedRows.Count == 0)
        {
            return rows;
        }

        var headerRow = usedRows.FirstOrDefault(row => RowLooksLikeHeader(row));
        if (headerRow is null)
        {
            return rows;
        }

        var serviceNameColumn = 0;
        var monthlyIdrColumn = 0;
        var yearlyIdrColumn = 0;

        foreach (var cell in headerRow.CellsUsed())
        {
            var header = NormalizeToken(cell.GetString());
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (serviceNameColumn == 0 && (header.Contains("service name") || header.Contains("description") || header.Contains("bill")))
            {
                serviceNameColumn = cell.Address.ColumnNumber;
            }

            if (monthlyIdrColumn == 0 && (header.Contains("monthly cost") && header.Contains("idr")))
            {
                monthlyIdrColumn = cell.Address.ColumnNumber;
            }

            if (yearlyIdrColumn == 0 && (header.Contains("yearly cost") && header.Contains("idr")))
            {
                yearlyIdrColumn = cell.Address.ColumnNumber;
            }
        }

        if (serviceNameColumn == 0 || (monthlyIdrColumn == 0 && yearlyIdrColumn == 0))
        {
            return rows;
        }

        var dataRows = usedRows.Where(r => r.RowNumber() > headerRow.RowNumber());
        foreach (var row in dataRows)
        {
            var description = row.Cell(serviceNameColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            if (description.Contains("domain", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var amountCellText = monthlyIdrColumn > 0
                ? row.Cell(monthlyIdrColumn).GetFormattedString()
                : string.Empty;

            if (!TryParseIdrAmount(amountCellText, out var amount) && yearlyIdrColumn > 0)
            {
                var yearlyText = row.Cell(yearlyIdrColumn).GetFormattedString();
                if (!TryParseIdrAmount(yearlyText, out amount))
                {
                    continue;
                }
            }

            rows.Add(new ParsedStatementRow(0, description, amount, null));
        }

        return rows;
    }

    private static bool RowLooksLikeHeader(IXLRow row)
    {
        var headers = row.CellsUsed()
            .Select(cell => NormalizeToken(cell.GetString()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var hasService = headers.Any(h => h.Contains("service name") || h.Contains("description") || h.Contains("bill"));
        var hasMonthlyIdr = headers.Any(h => h.Contains("monthly cost") && h.Contains("idr"));
        var hasYearlyIdr = headers.Any(h => h.Contains("yearly cost") && h.Contains("idr"));

        return hasService && (hasMonthlyIdr || hasYearlyIdr);
    }

    private static bool TryParseIdrAmount(string? raw, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim();
        if (cleaned == "-" || cleaned.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digitsOnly = new string(cleaned.Where(ch => char.IsDigit(ch) || ch == '-' || ch == ',' || ch == '.').ToArray());
        if (string.IsNullOrWhiteSpace(digitsOnly))
        {
            return false;
        }

        // IDR commonly uses '.' as thousand separators. Remove separators and parse as whole amount.
        var normalized = digitsOnly.Replace(".", string.Empty).Replace(",", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }

    private static string ExtractWorksheetText(IXLWorksheet worksheet)
    {
        var sb = new StringBuilder();
        var usedRows = worksheet.RowsUsed().ToList();
        
        if (usedRows.Count == 0)
        {
            return string.Empty;
        }

        // Find the maximum column used across all rows to ensure consistent column count
        var maxColumn = usedRows.SelectMany(r => r.CellsUsed()).Max(c => c.Address.ColumnNumber);

        foreach (var row in usedRows)
        {
            var rowCells = new List<string>();
            
            // Get cells for all columns up to maxColumn, filling empty cells with empty strings
            for (int col = 1; col <= maxColumn; col++)
            {
                var cell = row.Cell(col);
                var cellText = (cell?.GetString() ?? string.Empty).Trim();
                rowCells.Add(cellText);
            }

            var rowText = string.Join("\t", rowCells);
            if (!string.IsNullOrWhiteSpace(rowText))
            {
                sb.AppendLine(rowText);
            }
        }

        return sb.ToString();
    }

    private static IXLWorksheet SelectWorksheet(XLWorkbook workbook)
    {
        // Prefer an explicit sheet named "Detail" (case-insensitive)
        var detail = workbook.Worksheets.FirstOrDefault(ws => string.Equals(ws.Name?.Trim(), "Detail", StringComparison.OrdinalIgnoreCase));
        if (detail != null) return detail;

        // Otherwise prefer a sheet that appears to contain the transaction headers
        foreach (var ws in workbook.Worksheets)
        {
            try
            {
                if (WorksheetLooksLikeData(ws)) return ws;
            }
            catch
            {
                // ignore and continue
            }
        }

        // Fallback to first sheet
        return workbook.Worksheets.First();
    }

    private static bool WorksheetLooksLikeData(IXLWorksheet worksheet)
    {
        var usedRows = worksheet.RowsUsed()?.Take(12).ToList() ?? new List<IXLRow>();
        if (usedRows.Count == 0) return false;

        foreach (var row in usedRows)
        {
            int? descriptionColumn = null;
            int? amountColumn = null;

            foreach (var cell in row.CellsUsed())
            {
                var value = NormalizeToken(cell.GetString());
                if (string.IsNullOrEmpty(value)) continue;

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
                return true;
            }
        }

        return false;
    }

    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}