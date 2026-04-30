namespace ReconcileDocs.Contracts.ResponseModels.Documents;

public sealed record UploadDocumentResult(Guid DocumentUploadId, string StoredFileName, string StoragePath);