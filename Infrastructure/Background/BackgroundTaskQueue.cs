using System.Threading.Channels;
using ReconcileDocs.Application.Abstractions;

namespace ReconcileDocs.Infrastructure.Background;

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<(Guid RunId, Guid SpreadsheetUploadId, Guid StatementUploadId, string? Password)> _queue;

    public BackgroundTaskQueue(int capacity = 1000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false
        };
        _queue = Channel.CreateBounded<(Guid, Guid, Guid, string?)>(options);
    }

    public ValueTask EnqueueAsync(Guid runId, Guid spreadsheetUploadId, Guid statementUploadId, string? password)
    {
        var item = (runId, spreadsheetUploadId, statementUploadId, password);
        if (!_queue.Writer.TryWrite(item))
        {
            return _queue.Writer.WriteAsync(item);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<(Guid RunId, Guid SpreadsheetUploadId, Guid StatementUploadId, string? Password)> DequeueAsync(CancellationToken cancellationToken)
    {
        var job = await _queue.Reader.ReadAsync(cancellationToken);
        return job;
    }
}