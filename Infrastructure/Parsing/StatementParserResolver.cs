using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Infrastructure.Parsing;

public sealed class StatementParserResolver : IStatementParserResolver
{
    private readonly IEnumerable<IStatementParser> _parsers;

    public StatementParserResolver(IEnumerable<IStatementParser> parsers)
    {
        _parsers = parsers;
    }

    public IStatementParser Resolve(DocumentUpload upload, TemplateDefinition? template = null)
    {
        var exactParser = _parsers.FirstOrDefault(parser => parser.ParserKey != "generic" && parser.ParserKey != "template" && parser.CanParse(upload));
        if (exactParser is not null)
        {
            return exactParser;
        }

        if (!string.IsNullOrWhiteSpace(template?.ParserKey) && !string.Equals(template.ParserKey, "template", StringComparison.OrdinalIgnoreCase))
        {
            var configuredTemplateParser = _parsers.FirstOrDefault(parser => parser.ParserKey == template.ParserKey);
            if (configuredTemplateParser is not null)
            {
                return configuredTemplateParser;
            }
        }

        return _parsers.First(parser => parser.ParserKey == "generic");
    }
}