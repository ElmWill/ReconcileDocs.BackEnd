namespace ReconcileDocs.Application.Abstractions;

public interface IFileStorage
{
    Task<FileStorageResult> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken);
}

public sealed record FileStorageResult(string StoredFileName, string StoragePath);