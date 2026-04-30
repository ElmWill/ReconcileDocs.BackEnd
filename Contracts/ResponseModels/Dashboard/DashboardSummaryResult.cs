namespace ReconcileDocs.Contracts.ResponseModels.Dashboard;

public sealed record DashboardSummaryResult(int UploadedDocuments, int ReconcileRuns, int ActiveTemplates, int SuccessfulRuns, int FailedRuns);