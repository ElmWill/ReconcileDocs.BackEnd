using System.Security.Cryptography;

namespace ReconcileDocs.Application.Services;

public sealed class FileHashService
{
    public static string ComputeSHA256(byte[] fileContent)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(fileContent);
        return Convert.ToHexString(hash);
    }
}
