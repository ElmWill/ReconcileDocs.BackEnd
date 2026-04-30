namespace ReconcileDocs.Contracts.ResponseModels.Reconciles;

public sealed record ReconcileProgressResult(Guid RunId, int Status, int MatchedCount, int UnmatchedCount, int ErrorCount, DateTime StartedAtUtc, DateTime? CompletedAtUtc, string? ErrorMessage);