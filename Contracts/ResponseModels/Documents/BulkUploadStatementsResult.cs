namespace ReconcileDocs.Contracts.ResponseModels.Documents;

public sealed record BulkUploadFileInfo(
    string FileName,
    string FileHash,
    bool IsDuplicate,
    string? DuplicateOf,
    DateTime? DetectedPeriod,
    Guid DocumentUploadId
);

public sealed record BulkUploadStatementsResult(
    IReadOnlyList<BulkUploadFileInfo> UploadedFiles,
    IReadOnlyList<string> DuplicateHashes,
    Dictionary<string, List<BulkUploadFileInfo>> GroupsByPeriod
);
