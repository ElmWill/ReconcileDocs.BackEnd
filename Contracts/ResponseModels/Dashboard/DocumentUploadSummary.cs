namespace ReconcileDocs.Contracts.ResponseModels.Dashboard;

public sealed record DocumentUploadSummary(Guid Id, int DocumentKind, string OriginalFileName, string ContentType, long SizeBytes, DateTime UploadedAtUtc, int ReconcileStatus);