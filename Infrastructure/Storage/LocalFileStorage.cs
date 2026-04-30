using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ReconcileDocs.Application.Abstractions;

namespace ReconcileDocs.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<FileStorageResult> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var rootPath = _configuration["Storage:RootPath"] ?? "App_Data/uploads";
        var relativeDirectory = Path.Combine(rootPath, DateTime.UtcNow.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), DateTime.UtcNow.ToString("MM", System.Globalization.CultureInfo.InvariantCulture));
        var absoluteDirectory = Path.Combine(_environment.ContentRootPath, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var absoluteFilePath = Path.Combine(absoluteDirectory, storedFileName);

        await File.WriteAllBytesAsync(absoluteFilePath, content, cancellationToken);

        return new FileStorageResult(storedFileName, absoluteFilePath);
    }
}