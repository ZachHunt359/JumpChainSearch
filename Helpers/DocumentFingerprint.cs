using System.Security.Cryptography;
using System.Text;

namespace JumpChainSearch.Helpers;

public static class DocumentFingerprint
{
    private const int MinimumTextLength = 256;

    public static string? FromExtractedText(string? extractedText)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return null;

        var normalized = extractedText
            .Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length < MinimumTextLength)
            return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        return $"sha256:{hash}";
    }

    public static string? FromDriveChecksum(string? sha256Checksum)
    {
        var normalized = sha256Checksum?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : $"sha256:{normalized}";
    }
}