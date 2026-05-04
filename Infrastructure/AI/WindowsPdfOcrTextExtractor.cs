using Microsoft.Extensions.Logging;
using ReconcileDocs.Application.Abstractions;
using Windows.Globalization;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Foundation;

namespace ReconcileDocs.Infrastructure.AI;

public sealed class WindowsPdfOcrTextExtractor : IPdfOcrTextExtractor
{
    private readonly ILogger<WindowsPdfOcrTextExtractor> _logger;

    public WindowsPdfOcrTextExtractor(ILogger<WindowsPdfOcrTextExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ExtractPageTextsAsync(string pdfPath, string? password = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            // Normalize path: Windows OCR WinRT API rejects mixed separators (e.g., "path/to/file\name.pdf")
            var normalizedPath = Path.GetFullPath(pdfPath);
            _logger.LogInformation("Windows OCR: normalizing path from {OriginalPath} to {NormalizedPath}", pdfPath, normalizedPath);

            var storageFile = await StorageFile.GetFileFromPathAsync(normalizedPath);
            
            // Load PDF with optional password
            PdfDocument pdfDocument;
            try
            {
                pdfDocument = await PdfDocument.LoadFromFileAsync(storageFile);
            }
            catch (Exception ex) when (!string.IsNullOrEmpty(password))
            {
                // If loading without password fails and a password is provided, try with password
                _logger.LogInformation("Windows OCR: retrying PDF load with password due to {ExceptionType}", ex.GetType().Name);
                pdfDocument = await PdfDocument.LoadFromFileAsync(storageFile, password);
            }

            _logger.LogInformation("Windows OCR: loaded PDF {PdfPath} with {PageCount} pages", normalizedPath, pdfDocument.PageCount);

            var pageTexts = new List<string>(capacity: (int)pdfDocument.PageCount);
            var ocrEngine = OcrEngine.TryCreateFromLanguage(new Language("id-ID"))
                ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                ?? OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Language("en"))
                ?? OcrEngine.TryCreateFromLanguage(new Language("id"));
            if (ocrEngine is null)
            {
                _logger.LogWarning("Windows OCR engine could not be created");
                return Array.Empty<string>();
            }

            for (var pageIndex = 0u; pageIndex < pdfDocument.PageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var page = pdfDocument.GetPage(pageIndex);
                    using var renderStream = new InMemoryRandomAccessStream();

                    var renderOptions = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)Math.Max(page.Size.Width * 4, 2400),
                        DestinationHeight = (uint)Math.Max(page.Size.Height * 4, 2400)
                    };

                    await page.RenderToStreamAsync(renderStream, renderOptions);
                    renderStream.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(renderStream);
                    using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    var result = await ocrEngine.RecognizeAsync(bitmap);

                    var lines = BuildStructuredLines(result);

                    _logger.LogInformation("Windows OCR: page {PageIndex} recognized {LineCount} text lines", pageIndex, lines.Count);
                    pageTexts.Add(string.Join(Environment.NewLine, lines));
                }
                catch (Exception exPage)
                {
                    _logger.LogWarning(exPage, "Windows OCR: failed to OCR page {PageIndex} of {PdfPath}", pageIndex, pdfPath);
                    // add empty page text to keep page index alignment
                    pageTexts.Add(string.Empty);
                }
            }

            return pageTexts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows OCR extraction failed for PDF {PdfPath}", pdfPath);
            return Array.Empty<string>();
        }
    }

    private static List<string> BuildStructuredLines(OcrResult result)
    {
        var tokens = result.Lines
            .SelectMany(line => line.Words.Select(word => new OcrToken(
                word.Text,
                word.BoundingRect.Left,
                word.BoundingRect.Top,
                word.BoundingRect.Right,
                word.BoundingRect.Bottom)))
            .Where(token => !string.IsNullOrWhiteSpace(token.Text))
            .OrderBy(token => token.Top)
            .ThenBy(token => token.Left)
            .ToList();

        if (tokens.Count == 0)
        {
            return result.Lines
                .Select(line => string.Join(' ', line.Words.Select(word => word.Text)))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
        }

        var rows = new List<List<OcrToken>>();

        foreach (var token in tokens)
        {
            var centerY = (token.Top + token.Bottom) / 2.0;
            var row = rows.FirstOrDefault(existing =>
            {
                var existingCenterY = existing.Sum(item => (item.Top + item.Bottom) / 2.0) / existing.Count;
                return Math.Abs(existingCenterY - centerY) <= 18.0;
            });

            if (row is null)
            {
                row = new List<OcrToken>();
                rows.Add(row);
            }

            row.Add(token);
        }

        return rows
            .Select(row => string.Join(' ', row.OrderBy(token => token.Left).Select(token => token.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }

    private sealed record OcrToken(string Text, double Left, double Top, double Right, double Bottom);
}
