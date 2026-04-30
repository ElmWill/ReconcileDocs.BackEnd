using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class GenericStatementParser : IStatementParser
{
    public string ParserKey => "generic";

    public bool CanParse(DocumentUpload upload)
    {
        return true;
    }

    public Task<ParsedStatementResult> ParseAsync(DocumentUpload upload, byte[] content, string? password = null, CancellationToken cancellationToken = default)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        var rows = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select((line, index) => new ParsedStatementRow(index + 1, line.Trim(), 0m, null))
            .ToList();

        return Task.FromResult(new ParsedStatementResult(rows));
    }

    public async IAsyncEnumerable<ParsedStatementRow> ParseRowsAsync(DocumentUpload upload, byte[] content, string? password = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ParsedStatementRow(i + 1, lines[i], 0m, null);
        }
    }
}