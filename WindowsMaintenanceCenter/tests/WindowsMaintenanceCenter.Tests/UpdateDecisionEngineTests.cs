using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The update decision engine (spec sections 13, 14, 49). Every blocked case is a requirement: the
/// application may not recommend an update without a verified official source, a proven hardware
/// match and a positively established compatibility.
/// </summary>
public sealed class UpdateDecisionEngineTests
{
    private readonly UpdateDecisionEngine _engine = new();

    [Fact]
    public void Simulation_mode_never_produces_a_recommendation()
    {
        var result = _engine.Evaluate(Verified() with { SimulationMode = true });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.SimulationMode, result.BlockedReasonCode);
        Assert.False(result.CanDownload);
    }

    [Fact]
    public void Offline_mode_without_a_known_source_is_blocked()
    {
        var result = _engine.Evaluate(Base() with { OfflineMode = true });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.OfflineMode, result.BlockedReasonCode);
    }

    [Fact]
    public void Unknown_source_is_blocked()
    {
        var result = _engine.Evaluate(Verified() with { Source = ManufacturerSourceRef.Unknown() });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.ManufacturerSourceUnknown, result.BlockedReasonCode);
        Assert.False(result.CanDownload);
    }

    [Theory]
    [InlineData(SourceTrust.Unknown)]
    [InlineData(SourceTrust.ThirdParty)]
    public void Trust_below_the_minimum_is_blocked(SourceTrust trust)
    {
        var result = _engine.Evaluate(Verified() with { Trust = trust });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.ThirdPartySource, result.BlockedReasonCode);
    }

    [Theory]
    [InlineData(VerificationLevel.NotVerified)]
    [InlineData(VerificationLevel.SourceReachable)]
    public void Verification_below_the_minimum_is_blocked(VerificationLevel level)
    {
        var result = _engine.Evaluate(Verified() with { Verification = level });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.ManufacturerSourceNotVerifiable, result.BlockedReasonCode);
    }

    [Fact]
    public void Unproven_hardware_match_is_blocked()
    {
        var result = _engine.Evaluate(Verified() with { IsHardwareMatchProven = false });

        Assert.Equal(UpdateStatus.Blocked, result.Status);
        Assert.Equal(BlockReasons.HardwareMatchNotProven, result.BlockedReasonCode);
    }

    [Fact]
    public void Explicitly_incompatible_candidate_is_not_offered()
    {
        var result = _engine.Evaluate(Verified() with { IsCompatible = false });

        Assert.Equal(UpdateStatus.Incompatible, result.Status);
        Assert.False(result.CanDownload);
    }

    [Fact]
    public void Unknown_compatibility_is_not_treated_as_compatible()
    {
        // The candidate has to be newer than the installed version: with the same version the engine
        // answers "current", and that answer says nothing about compatibility. The fixture starts at
        // 1.0, so the case described by this test needs 2.0.
        var result = _engine.Evaluate(Verified() with { IsCompatible = null, Available = Version("2.0") });

        Assert.Equal(UpdateStatus.Warning, result.Status);
        Assert.False(result.CanDownload);
        Assert.Equal("Update_Reason_CompatibilityUnproven", result.Reason.Key);
    }

    [Fact]
    public void Unreadable_versions_produce_unknown_instead_of_a_recommendation()
    {
        var result = _engine.Evaluate(Verified() with { Installed = new VersionInfo() });

        Assert.Equal(UpdateStatus.Unknown, result.Status);
        Assert.Equal("Update_Reason_VersionUnknown", result.Reason.Key);
        Assert.False(result.CanDownload);
    }

    [Fact]
    public void Same_version_is_current()
    {
        var result = _engine.Evaluate(Verified());
        Assert.Equal(UpdateStatus.Current, result.Status);
        Assert.False(result.CanDownload);
    }

    [Fact]
    public void Stale_facts_must_be_refreshed_before_a_download()
    {
        var result = _engine.Evaluate(Verified() with
        {
            Freshness = Freshness.Stale,
            Available = Version("2.0"),
        });

        Assert.Equal(UpdateStatus.Unknown, result.Status);
        Assert.True(result.RequiresRefresh);
        Assert.False(result.CanDownload);
        Assert.Contains(result.Evidence, e => e.Contains("refresh required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fresh_newer_version_without_download_url_cannot_be_downloaded()
    {
        var result = _engine.Evaluate(Verified() with
        {
            Available = Version("2.0"),
            HasDownloadUrl = false,
        });

        Assert.Equal(UpdateStatus.Optional, result.Status);
        Assert.False(result.CanDownload);
    }

    [Fact]
    public void Fresh_newer_security_update_is_offered_for_download()
    {
        var result = _engine.Evaluate(Verified() with
        {
            Available = Version("2.0"),
            HasDownloadUrl = true,
            IsSecurityRelevant = true,
            Freshness = Freshness.Recent,
        });

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.CanDownload);
        Assert.Equal("Update_Reason_UpdateAvailable", result.Reason.Key);
    }

    private static UpdateDecisionInput Base() => new()
    {
        ComponentId = "gpu-1",
        DeviceName = TextInfo.Known("NVIDIA GeForce RTX 3060", ValueOrigin.Wmi(DateTimeOffset.UnixEpoch, "Win32_VideoController")),
        Installed = Version("1.0"),
        Available = Version("1.0"),
        Evidence = new[] { "source=official" },
    };

    /// <summary>Everything established: the case that may produce a recommendation.</summary>
    private static UpdateDecisionInput Verified() => Base() with
    {
        Source = new ManufacturerSourceRef
        {
            AdapterId = "nvidia",
            DisplayNameKey = "Vendor_NVIDIA",
            Url = "https://www.nvidia.com/",
            Trust = SourceTrust.VerifiedOem,
            Verification = VerificationLevel.MetadataMatch,
            RetrievedAt = DateTimeOffset.UnixEpoch,
        },
        Trust = SourceTrust.VerifiedOem,
        Verification = VerificationLevel.MetadataMatch,
        IsHardwareMatchProven = true,
        IsCompatible = true,
        Freshness = Freshness.Recent,
    };

    private static VersionInfo Version(string value) => new()
    {
        Raw = TextInfo.Known(value, ValueOrigin.Manufacturer(DateTimeOffset.UnixEpoch, "nvidia", VerificationLevel.MetadataMatch)),
        Normalized = TextInfo.Known(value, ValueOrigin.Manufacturer(DateTimeOffset.UnixEpoch, "nvidia", VerificationLevel.MetadataMatch)),
    };
}
