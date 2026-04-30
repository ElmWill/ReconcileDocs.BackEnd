namespace ReconcileDocs.Contracts.ResponseModels.Reconciles;

public sealed record BulkReconcileResult(
    IReadOnlyList<string> EnqueuedRunIds,
    int TotalEnqueued
);
