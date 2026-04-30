using MediatR;
using Microsoft.EntityFrameworkCore;
using ReconcileDocs.Application.Abstractions;
using ReconcileDocs.Application.Services;
using ReconcileDocs.Contracts.RequestModels.Reconciles;
using ReconcileDocs.Contracts.ResponseModels.Reconciles;
using ReconcileDocs.Domain.Entities;

namespace ReconcileDocs.Application.Features.Reconciles;

public sealed class StartReconcileHandler : IRequestHandler<StartReconcileCommand, StartReconcileResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ReconcileProcessor _processor;

    public StartReconcileHandler(IApplicationDbContext dbContext, ReconcileProcessor processor)
    {
        _dbContext = dbContext;
        _processor = processor;
    }

    public async Task<StartReconcileResult> Handle(StartReconcileCommand request, CancellationToken cancellationToken)
    {
        // create run placeholder
        var run = new ReconcileRun
        {
            Id = Guid.NewGuid(),
            SpreadsheetUploadId = request.SpreadsheetUploadId,
            StatementUploadId = request.StatementUploadId,
            ParserName = string.Empty,
            MatchedCount = 0,
            UnmatchedCount = 0,
            ErrorCount = 0,
            Status = 1,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = null
        };

        _dbContext.ReconcileRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // delegate processing to the processor (runs synchronously in this request)
        return await _processor.ExecuteAsync(run, request.SpreadsheetUploadId, request.StatementUploadId, request.Password, cancellationToken);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}