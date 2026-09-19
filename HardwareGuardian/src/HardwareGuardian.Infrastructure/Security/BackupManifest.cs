using HardwareGuardian.Core;
using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Infrastructure.Security;

/// <summary>
/// Manifest of one backup, written next to the artefacts. It is the only source a rollback may use:
/// if the manifest is missing or unreadable, no rollback is offered (fail closed).
/// </summary>
public sealed record BackupManifest
{
    public string Id { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public OperationKind Kind { get; init; }

    public RiskLevel Risk { get; init; }

    public ComponentCategory Category { get; init; }

    public string? ComponentId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string ReasonKey { get; init; } = string.Empty;

    /// <summary>Sequence number of the created system restore point, when one was created.</summary>
    public string? RestorePointSequence { get; init; }

    /// <summary>Registry exports (<c>.reg</c> files) created before the change.</summary>
    public IReadOnlyList<string> RegistryExports { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();

    /// <summary>Path of the captured driver/device state, when one was requested.</summary>
    public string? DriverSnapshotPath { get; init; }

    /// <summary>Human readable restore instructions, in the order in which they must be applied.</summary>
    public IReadOnlyList<string> RestoreSteps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public string ApplicationVersion { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;
}
