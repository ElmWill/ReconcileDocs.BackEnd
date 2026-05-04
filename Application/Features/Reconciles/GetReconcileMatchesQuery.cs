using MediatR;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class GetReconcileMatchesQuery : IRequest<GetReconcileMatchesResult>
{
    public Guid RunId { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    public bool? MatchedOnly { get; set; }
}

public sealed record ReconcileMatchDto(
    Guid Id,
    int SpreadsheetRowNumber,
    int StatementRowNumber,
    string Description,
    decimal Amount,
    bool IsMatched,
    string? SpreadsheetDescription,
    decimal? SpreadsheetAmount,
    DateOnly? SpreadsheetTransactionDate,
    string? StatementDescription,
    decimal StatementAmount,
    DateOnly? StatementTransactionDate);

public sealed record GetReconcileMatchesResult(IReadOnlyList<ReconcileMatchDto> Matches, int Total);
