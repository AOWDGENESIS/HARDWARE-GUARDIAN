using System.Security.Cryptography;
using System.Text;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Infrastructure.Security;

/// <summary>Hash computation used for download verification and release checksums (spec section 60).</summary>
public sealed class HashService : IHashService
{
    private readonly IClock _clock;

    public HashService(IClock clock)
    {
        _clock = clock;
    }

    public async Task<HashResult> ComputeFileHashAsync(
        string path,
        string algorithm = "SHA256",
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return HashResult.Failed(path ?? string.Empty, algorithm, "file does not exist");
        }

        try
        {
            var info = new FileInfo(path);
            var origin = ValueOrigin.LocalFile(_clock.Now, path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);

            using var hasher = CreateHasher(algorithm);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hasher.TransformBlock(buffer, 0, read, null, 0);
                total += read;
                if (progress is not null && info.Length > 0)
                {
                    progress.Report(Math.Min(1d, (double)total / info.Length));
                }
            }

            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            return new HashResult
            {
                Path = path,
                Algorithm = algorithm.ToUpperInvariant(),
                Hash = Convert.ToHexString(hasher.Hash ?? Array.Empty<byte>()),
                SizeBytes = Measured<long>.Known(info.Length, origin),
                ComputedAt = _clock.Now,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return HashResult.Failed(path, algorithm, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public string ComputeStringHash(string content, string algorithm = "SHA256")
    {
        using var hasher = CreateHasher(algorithm);
        var hash = hasher.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(hash);
    }

    public bool IsValidHashFormat(string? hash, string algorithm = "SHA256")
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var expectedLength = algorithm.ToUpperInvariant() switch
        {
            "MD5" => 32,
            "SHA1" => 40,
            "SHA256" => 64,
            "SHA384" => 96,
            "SHA512" => 128,
            _ => 64,
        };

        return hash.Length == expectedLength && hash.All(Uri.IsHexDigit);
    }

    private static HashAlgorithm CreateHasher(string algorithm) => algorithm.ToUpperInvariant() switch
    {
        "SHA1" => SHA1.Create(),
        "SHA384" => SHA384.Create(),
        "SHA512" => SHA512.Create(),
        "SHA256" => SHA256.Create(),
        _ => SHA256.Create(),
    };
}
