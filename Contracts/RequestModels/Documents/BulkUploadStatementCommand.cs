using MediatR;
using ReconcileDocs.Contracts.ResponseModels.Documents;

namespace ReconcileDocs.Contracts.RequestModels.Documents;

public sealed record BulkUploadStatementCommand(
    IReadOnlyList<byte[]> FileContents,
    IReadOnlyList<string> FileNames,
    IReadOnlyList<string> ContentTypes,
    Guid SpreadsheetUploadId
) : IRequest<BulkUploadStatementsResult>;
