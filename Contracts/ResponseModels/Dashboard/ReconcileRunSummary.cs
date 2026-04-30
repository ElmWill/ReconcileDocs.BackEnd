namespace ReconcileDocs.Contracts.ResponseModels.Dashboard;

public sealed record ReconcileRunSummary(Guid Id, Guid SpreadsheetUploadId, Guid StatementUploadId, string ParserName, int MatchedCount, int UnmatchedCount, int ErrorCount, int Status, DateTime StartedAtUtc, DateTime? CompletedAtUtc);