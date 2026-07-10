using System.Security.Cryptography;
using System.Text;

namespace AiMemory.Core;

/// <summary>Stable content hashing used for change-detection (skip re-embedding unchanged text).</summary>
public static class Hashing
{
    public static string ContentHash(string? text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes);
    }
}
