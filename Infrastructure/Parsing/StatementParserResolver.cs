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
        if (!string.IsNullOrWhiteSpace(template?.ParserKey))
        {
            var configuredTemplateParser = _parsers.FirstOrDefault(parser => parser.ParserKey == template.ParserKey);
            if (configuredTemplateParser is not null)
            {
                return configuredTemplateParser;
            }
        }

        var templateFallbackParser = _parsers.FirstOrDefault(parser => parser.ParserKey == "template");
        if (templateFallbackParser is not null && template is not null)
        {
            return templateFallbackParser;
        }

        var templateParser = _parsers.FirstOrDefault(parser => parser.ParserKey == "template" && parser.CanParse(upload));
        if (templateParser is not null)
        {
            return templateParser;
        }

        var exactParser = _parsers.FirstOrDefault(parser => parser.ParserKey != "generic" && parser.ParserKey != "template" && parser.CanParse(upload));
        if (exactParser is not null)
        {
            return exactParser;
        }

        return _parsers.First(parser => parser.ParserKey == "generic");
    }
}