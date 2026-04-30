using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Contracts.RequestModels.Templates;
using ReconcileDocs.Contracts.ResponseModels.Templates;
using ReconcileDocs.Domain;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReconcileDocs.Application.Features.Templates;

public sealed class SuggestTemplateFromUploadHandler : IRequestHandler<SuggestTemplateFromUploadCommand, SuggestTemplateFromUploadResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStatementParserResolver _parserResolver;

    public SuggestTemplateFromUploadHandler(IApplicationDbContext dbContext, IStatementParserResolver parserResolver)
    {
        _dbContext = dbContext;
        _parserResolver = parserResolver;
    }

    public async Task<SuggestTemplateFromUploadResult> Handle(SuggestTemplateFromUploadCommand request, CancellationToken cancellationToken)
    {
        var upload = await _dbContext.DocumentUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.UploadId, cancellationToken);

        if (upload is null)
        {
            throw new KeyNotFoundException("Upload not found.");
        }

        if (!File.Exists(upload.StoragePath))
        {
            throw new FileNotFoundException("Uploaded file is missing from storage.", upload.StoragePath);
        }

        var content = await File.ReadAllBytesAsync(upload.StoragePath, cancellationToken);
        var parser = _parserResolver.Resolve(upload);
        var parsed = await parser.ParseAsync(upload, content, null, cancellationToken);

        var descriptionSamples = parsed.Rows
            .Select(row => row.Description?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(50)
            .Select(value => value!)
            .ToList();

        var datePatterns = InferDatePatterns(descriptionSamples);
        var documentKind = upload.DocumentKind == (int)DocumentKind.Spreadsheet ? DocumentKind.Spreadsheet : DocumentKind.StatementPdf;
        var skipRows = documentKind == DocumentKind.Spreadsheet ? 1 : 0;

        var config = new
        {
            sourceUploadId = upload.Id,
            parserUsed = parser.ParserKey,
            skipRows,
            datePatterns,
            descriptionColumnHints = new[] { "description", "narration", "remark", "details" },
            amountColumnHints = new[] { "amount", "debit", "credit", "value" }
        };

        var suggestedJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var baseName = Path.GetFileNameWithoutExtension(upload.OriginalFileName);

        return new SuggestTemplateFromUploadResult(
            $"{baseName} template",
            documentKind,
            "template",
            skipRows,
            datePatterns,
            new[] { "description", "narration", "remark", "details" },
            new[] { "amount", "debit", "credit", "value" },
            suggestedJson);
    }

    private static List<string> InferDatePatterns(IEnumerable<string> lines)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, "\\b\\d{2}/\\d{2}/\\d{4}\\b"))
            {
                patterns.Add("dd/MM/yyyy");
            }

            if (Regex.IsMatch(line, "\\b\\d{4}-\\d{2}-\\d{2}\\b"))
            {
                patterns.Add("yyyy-MM-dd");
            }

            if (Regex.IsMatch(line, "\\b\\d{2}-\\d{2}-\\d{4}\\b"))
            {
                patterns.Add("dd-MM-yyyy");
            }
        }

        if (patterns.Count == 0)
        {
            patterns.Add("dd/MM/yyyy");
            patterns.Add("yyyy-MM-dd");
        }

        return patterns.ToList();
    }
}
