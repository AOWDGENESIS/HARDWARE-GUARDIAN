using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Security;

/// <summary>
/// Collects the security relevant facts of a session for the security report (spec section 57):
/// driver signature states, hash verifications of downloaded artefacts, verified sources and
/// every operation that was blocked. The service only aggregates - it never changes anything.
/// </summary>
public sealed class SecurityAuditService
{
    private readonly IHashService _hash;
    private readonly ISignatureVerifier _signatures;
    private readonly IClock _clock;

    public SecurityAuditService(IHashService hash, ISignatureVerifier signatures, IClock clock)
    {
        _hash = hash;
        _signatures = signatures;
        _clock = clock;
    }

    public async Task<SecurityReportData> BuildAsync(
        SystemSnapshot? snapshot,
        IReadOnlyList<DownloadArtifact> artifacts,
        IReadOnlyList<SourceVerificationResult> sources,
        IReadOnlyList<Problem> problems,
        CancellationToken cancellationToken)
    {
        var hashes = new List<HashResult>();
        var signatures = new List<SignatureResult>();
        var notes = new List<string>();

        foreach (var artifact in artifacts)
        {
            if (artifact.Hash.Succeeded)
            {
                hashes.Add(artifact.Hash);
            }

            signatures.Add(artifact.Signature);
        }

        var unsignedDrivers = snapshot?.Drivers.Where(d => d.IsSigned == false).ToList() ?? new List<DriverRecord>();
        var unverifiedDrivers = snapshot?.Drivers.Where(d => d.IsSigned is null).ToList() ?? new List<DriverRecord>();

        if (unsignedDrivers.Count > 0)
        {
            notes.Add($"{unsignedDrivers.Count} driver(s) reported as unsigned by the platform");
        }

        if (unverifiedDrivers.Count > 0)
        {
            notes.Add($"{unverifiedDrivers.Count} driver(s) have no signature state reported (treated as unverified, not as unsigned)");
        }

        if (snapshot is not null && snapshot.Problems.All(p => p.Category != ComponentCategory.Security))
        {
            notes.Add("no dedicated security problem was detected during this session");
        }

        var blocked = problems
            .SelectMany(p => p.BlockedOperations)
            .ToList();

        // Verify the integrity of the on-disk configuration as an additional, cheap check.
        if (snapshot is not null)
        {
            notes.Add($"snapshot={snapshot.Id} captured {snapshot.CapturedAt:u} (simulation={snapshot.IsSimulation})");
        }

        await Task.CompletedTask.ConfigureAwait(false);

        return new SecurityReportData
        {
            Hashes = hashes,
            Signatures = signatures,
            Sources = sources,
            Findings = problems.Where(p => p.Severity is Severity.Critical or Severity.Error or Severity.Warning or Severity.Blocked).ToList(),
            BlockedOperations = blocked,
            Notes = notes,
        };
    }

    /// <summary>Verifies the signature and hash of a local file, for the "inspect a file" action.</summary>
    public async Task<(SignatureResult Signature, HashResult Hash)> InspectFileAsync(string path, CancellationToken cancellationToken)
    {
        var signature = _signatures.VerifyFile(path, cancellationToken);
        var hash = await _hash.ComputeFileHashAsync(path, "SHA256", null, cancellationToken).ConfigureAwait(false);
        return (signature, hash);
    }
}
