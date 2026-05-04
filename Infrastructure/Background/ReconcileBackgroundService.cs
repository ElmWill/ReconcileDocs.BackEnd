using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace ReconcileDocs.Infrastructure.Background;

public sealed class ReconcileBackgroundService : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReconcileBackgroundService> _logger;

    public ReconcileBackgroundService(IBackgroundTaskQueue queue, IServiceProvider serviceProvider, ILogger<ReconcileBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueAsync(stoppingToken);
                var runId = item.RunId;
                var spreadsheetUploadId = item.SpreadsheetUploadId;
                var statementUploadId = item.StatementUploadId;
                var password = item.Password;

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var processor = scope.ServiceProvider.GetRequiredService<ReconcileProcessor>();

                // load run entity
                var run = await db.ReconcileRuns.FirstAsync(r => r.Id == runId, stoppingToken);

                await processor.ExecuteAsync(run, spreadsheetUploadId, statementUploadId, password, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log and continue processing next job
                _logger.LogError(ex, "Reconcile background worker failed processing a job");
            }
        }
    }
}