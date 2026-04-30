namespace ReconcileDocs.Contracts.ResponseModels.Reconciles;

public sealed record StartReconcileResult(Guid ReconcileRunId, string ParserName, int MatchedCount, int UnmatchedCount, int ErrorCount);