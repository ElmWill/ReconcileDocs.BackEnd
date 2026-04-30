using System.Threading;
using System.Threading.Tasks;

namespace ReconcileDocs.Application.Abstractions;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Guid runId, Guid spreadsheetUploadId, Guid statementUploadId, string? password);
    ValueTask<(Guid RunId, Guid SpreadsheetUploadId, Guid StatementUploadId, string? Password)> DequeueAsync(CancellationToken cancellationToken);
}