using ReconcileDocs.Domain;

namespace ReconcileDocs.Contracts.ResponseModels.Templates;

public sealed record SuggestTemplateFromUploadResult(
    string NameSuggestion,
    DocumentKind DocumentKind,
    string ParserKey,
    int SkipRows,
    IReadOnlyList<string> DatePatterns,
    IReadOnlyList<string> DescriptionColumnHints,
    IReadOnlyList<string> AmountColumnHints,
    string SuggestedConfigurationJson);
