using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Manufacturer;

/// <summary>Base implementation of a manufacturer adapter.</summary>
public abstract class ManufacturerAdapterBase : IManufacturerAdapter
{
    protected ManufacturerAdapterBase(IClock clock)
    {
        Clock = clock;
    }

    protected IClock Clock { get; }

    public abstract string Id { get; }

    public abstract string DisplayNameKey { get; }

    public abstract SourceTrust Trust { get; }

    public abstract IReadOnlyList<ComponentCategory> Categories { get; }

    public abstract IReadOnlyList<string> VendorIds { get; }

    public abstract IReadOnlyList<string> NamePatterns { get; }

    public abstract ManufacturerSource? Source { get; }

    /// <summary>
    /// Most vendors do not publish machine readable data. Only adapters that really query a
    /// documented endpoint override this.
    /// </summary>
    public virtual bool SupportsAutomatedCheck => false;

    public virtual bool RequiresNetwork => true;

    public virtual Task<SourceCheckResult> CheckAsync(ManufacturerQuery query, CancellationToken cancellationToken)
    {
        var source = Source;
        var note = source is null || !source.IsConfigured
            ? LocalizedText.Of("Source_Check_NotConfigured", DisplayNameKey)
            : LocalizedText.Of("Source_Check_ManualOnly", source.DisplayNameKey);

        return Task.FromResult(new SourceCheckResult
        {
            AdapterId = Id,
            DisplayNameKey = DisplayNameKey,
            Trust = source is null || !source.IsConfigured ? SourceTrust.Unknown : Trust,
            Verification = VerificationLevel.NotVerified,
            Succeeded = false,
            Freshness = Freshness.Unknown,
            RetrievedAt = Clock.Now,
            Note = note,
        });
    }

    protected ManufacturerSourceRef DescribeSourceRef(VerificationLevel achieved) => new()
    {
        AdapterId = Id,
        DisplayNameKey = DisplayNameKey,
        Url = Source?.IsConfigured == true ? Source.LandingUrl : null,
        Trust = Source?.IsConfigured == true ? Trust : SourceTrust.Unknown,
        Verification = achieved,
        RetrievedAt = Clock.Now,
        Note = Source?.Notes,
    };
}

/// <summary>AMD: processors, chipsets and Radeon graphics.</summary>
public sealed class AmdAdapter : ManufacturerAdapterBase
{
    public AmdAdapter(IClock clock) : base(clock)
    {
    }

    public override string Id => "amd";

    public override string DisplayNameKey => "Vendor_AMD";

    public override SourceTrust Trust => SourceTrust.Manufacturer;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Cpu, ComponentCategory.Graphics, ComponentCategory.Chipset, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => new[] { "1002", "1022" };

    public override IReadOnlyList<string> NamePatterns => new[] { "AMD", "Advanced Micro Devices", "Radeon", "Ryzen", "Athlon" };

    public override ManufacturerSource? Source => ManufacturerSources.Amd;
}

/// <summary>NVIDIA: graphics adapters.</summary>
public sealed class NvidiaAdapter : ManufacturerAdapterBase
{
    public NvidiaAdapter(IClock clock) : base(clock)
    {
    }

    public override string Id => "nvidia";

    public override string DisplayNameKey => "Vendor_NVIDIA";

    public override SourceTrust Trust => SourceTrust.Manufacturer;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Graphics, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => new[] { "10DE" };

    public override IReadOnlyList<string> NamePatterns => new[] { "NVIDIA", "GeForce", "Quadro", "RTX", "GTX" };

    public override ManufacturerSource? Source => ManufacturerSources.Nvidia;
}

/// <summary>Intel: processors, graphics, wireless and chipsets.</summary>
public sealed class IntelAdapter : ManufacturerAdapterBase
{
    public IntelAdapter(IClock clock) : base(clock)
    {
    }

    public override string Id => "intel";

    public override string DisplayNameKey => "Vendor_Intel";

    public override SourceTrust Trust => SourceTrust.Manufacturer;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Cpu, ComponentCategory.Graphics, ComponentCategory.Network, ComponentCategory.Chipset, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => new[] { "8086" };

    public override IReadOnlyList<string> NamePatterns => new[] { "Intel", "Intel(R) Corporation" };

    public override ManufacturerSource? Source => ManufacturerSources.Intel;
}

/// <summary>Mainboard vendor adapter (BIOS and chipset drivers).</summary>
public sealed class BoardVendorAdapter : ManufacturerAdapterBase
{
    private readonly string _id;
    private readonly string _displayNameKey;
    private readonly ManufacturerSource _source;

    public BoardVendorAdapter(IClock clock, string id, string displayNameKey, IEnumerable<string> namePatterns, ManufacturerSource source)
        : base(clock)
    {
        _id = id;
        _displayNameKey = displayNameKey;
        _source = source;
        NamePatterns = namePatterns.ToList();
    }

    public override string Id => _id;

    public override string DisplayNameKey => _displayNameKey;

    public override SourceTrust Trust => SourceTrust.Manufacturer;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Motherboard, ComponentCategory.Bios, ComponentCategory.Chipset, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => Array.Empty<string>();

    public override IReadOnlyList<string> NamePatterns { get; }

    public override ManufacturerSource? Source => _source;
}

/// <summary>OEM system vendor adapter (BIOS, firmware, OEM drivers).</summary>
public sealed class OemVendorAdapter : ManufacturerAdapterBase
{
    private readonly string _id;
    private readonly string _displayNameKey;
    private readonly ManufacturerSource _source;

    public OemVendorAdapter(IClock clock, string id, string displayNameKey, IEnumerable<string> namePatterns, ManufacturerSource source)
        : base(clock)
    {
        _id = id;
        _displayNameKey = displayNameKey;
        _source = source;
        NamePatterns = namePatterns.ToList();
    }

    public override string Id => _id;

    public override string DisplayNameKey => _displayNameKey;

    public override SourceTrust Trust => SourceTrust.VerifiedOem;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.System, ComponentCategory.Bios, ComponentCategory.Firmware, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => Array.Empty<string>();

    public override IReadOnlyList<string> NamePatterns { get; }

    public override ManufacturerSource? Source => _source;
}

/// <summary>Storage vendor adapter (firmware and vendor health tools).</summary>
public sealed class StorageVendorAdapter : ManufacturerAdapterBase
{
    private readonly string _id;
    private readonly string _displayNameKey;
    private readonly ManufacturerSource _source;

    public StorageVendorAdapter(IClock clock, string id, string displayNameKey, IEnumerable<string> namePatterns, ManufacturerSource source)
        : base(clock)
    {
        _id = id;
        _displayNameKey = displayNameKey;
        _source = source;
        NamePatterns = namePatterns.ToList();
    }

    public override string Id => _id;

    public override string DisplayNameKey => _displayNameKey;

    public override SourceTrust Trust => SourceTrust.Manufacturer;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Storage, ComponentCategory.Firmware, ComponentCategory.Driver };

    public override IReadOnlyList<string> VendorIds => Array.Empty<string>();

    public override IReadOnlyList<string> NamePatterns { get; }

    public override ManufacturerSource? Source => _source;
}

/// <summary>Vendor without a documented source: reported as SOURCE UNKNOWN.</summary>
public sealed class UnknownSourceAdapter : ManufacturerAdapterBase
{
    private readonly string _id;
    private readonly string _displayNameKey;

    public UnknownSourceAdapter(IClock clock, string id, string displayNameKey, IEnumerable<string> namePatterns)
        : base(clock)
    {
        _id = id;
        _displayNameKey = displayNameKey;
        NamePatterns = namePatterns.ToList();
    }

    public override string Id => _id;

    public override string DisplayNameKey => _displayNameKey;

    public override SourceTrust Trust => SourceTrust.Unknown;

    public override IReadOnlyList<ComponentCategory> Categories => new[] { ComponentCategory.Driver, ComponentCategory.System };

    public override IReadOnlyList<string> VendorIds => Array.Empty<string>();

    public override IReadOnlyList<string> NamePatterns { get; }

    public override ManufacturerSource? Source => ManufacturerSources.UnknownSource(_id, _displayNameKey);
}

/// <summary>Resolves a manufacturer string or hardware id to an adapter (spec section 11).</summary>
public sealed class ManufacturerResolver : IManufacturerResolver
{
    private readonly IReadOnlyList<IManufacturerAdapter> _adapters;

    public ManufacturerResolver(IClock clock)
    {
        _adapters = new List<IManufacturerAdapter>
        {
            new AmdAdapter(clock),
            new NvidiaAdapter(clock),
            new IntelAdapter(clock),
            new BoardVendorAdapter(clock, "gigabyte", "Vendor_GIGABYTE", new[] { "GIGABYTE", "Giga-Byte" }, ManufacturerSources.Gigabyte),
            new BoardVendorAdapter(clock, "asus", "Vendor_ASUS", new[] { "ASUSTeK", "ASUS" }, ManufacturerSources.Asus),
            new BoardVendorAdapter(clock, "msi", "Vendor_MSI", new[] { "Micro-Star", "MSI" }, ManufacturerSources.Msi),
            new BoardVendorAdapter(clock, "asrock", "Vendor_ASRock", new[] { "ASRock" }, ManufacturerSources.Asrock),
            new OemVendorAdapter(clock, "dell", "Vendor_Dell", new[] { "Dell", "Alienware" }, ManufacturerSources.Dell),
            new OemVendorAdapter(clock, "hp", "Vendor_HP", new[] { "HP", "Hewlett-Packard", "HPE" }, ManufacturerSources.Hp),
            new OemVendorAdapter(clock, "lenovo", "Vendor_Lenovo", new[] { "LENOVO", "ThinkPad", "IdeaPad" }, ManufacturerSources.Lenovo),
            new OemVendorAdapter(clock, "acer", "Vendor_Acer", new[] { "Acer", "Predator" }, ManufacturerSources.Acer),
            new OemVendorAdapter(clock, "microsoft", "Vendor_Microsoft", new[] { "Microsoft", "Surface" }, ManufacturerSources.Microsoft),
            new StorageVendorAdapter(clock, "samsung", "Vendor_Samsung", new[] { "Samsung" }, ManufacturerSources.Samsung),
            new StorageVendorAdapter(clock, "kingston", "Vendor_Kingston", new[] { "Kingston" }, ManufacturerSources.Kingston),
            new StorageVendorAdapter(clock, "crucial", "Vendor_Crucial", new[] { "Crucial", "Micron" }, ManufacturerSources.Crucial),
            new StorageVendorAdapter(clock, "westerndigital", "Vendor_WesternDigital", new[] { "WDC", "Western Digital", "SanDisk" }, ManufacturerSources.WesternDigital),
            new StorageVendorAdapter(clock, "seagate", "Vendor_Seagate", new[] { "Seagate", "ST" }, ManufacturerSources.Seagate),
            new UnknownSourceAdapter(clock, "realtek", "Vendor_Realtek", new[] { "Realtek" }),
            new UnknownSourceAdapter(clock, "qualcomm", "Vendor_Qualcomm", new[] { "Qualcomm", "Snapdragon" }),
            new UnknownSourceAdapter(clock, "mediatek", "Vendor_MediaTek", new[] { "MediaTek" }),
        };
    }

    public IReadOnlyList<IManufacturerAdapter> Adapters => _adapters;

    public IManufacturerAdapter? Resolve(string? manufacturer, string? model, ComponentCategory category)
    {
        var haystack = $"{manufacturer} {model}".Trim();
        if (haystack.Length == 0)
        {
            return null;
        }

        var matches = _adapters
            .Where(a => a.Categories.Contains(category) || a.Categories.Contains(ComponentCategory.Driver))
            .Where(a => a.NamePatterns.Any(pattern => haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Prefer the adapter with the most specific (longest) matching pattern.
        return matches
            .OrderByDescending(a => a.NamePatterns.Where(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)).Max(p => p.Length))
            .ThenByDescending(a => a.Trust)
            .FirstOrDefault();
    }

    public IManufacturerAdapter? ResolveByHardwareId(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return null;
        }

        var vendor = ExtractVendorId(hardwareId);
        return vendor is null ? null : _adapters.FirstOrDefault(a => a.VendorIds.Contains(vendor, StringComparer.OrdinalIgnoreCase));
    }

    public ManufacturerSourceRef DescribeSource(IManufacturerAdapter? adapter, VerificationLevel achieved = VerificationLevel.NotVerified)
    {
        if (adapter is null)
        {
            return ManufacturerSourceRef.Unknown("no adapter matched the detected hardware");
        }

        var source = adapter.Source;
        return new ManufacturerSourceRef
        {
            AdapterId = adapter.Id,
            DisplayNameKey = adapter.DisplayNameKey,
            Url = source?.IsConfigured == true ? source.LandingUrl : null,
            Trust = source?.IsConfigured == true ? adapter.Trust : SourceTrust.Unknown,
            Verification = achieved,
            RetrievedAt = null,
            Note = source?.Notes,
        };
    }

    /// <summary>Extracts the PCI/USB vendor id from a hardware id such as PCI\VEN_10DE&amp;DEV_2504.</summary>
    public static string? ExtractVendorId(string hardwareId)
    {
        var marker = hardwareId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0 && hardwareId.Length >= marker + 8)
        {
            return hardwareId.Substring(marker + 4, 4).ToUpperInvariant();
        }

        var vid = hardwareId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        return vid >= 0 && hardwareId.Length >= vid + 8 ? hardwareId.Substring(vid + 4, 4).ToUpperInvariant() : null;
    }
}
