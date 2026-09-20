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
    /// <summary>Name variants reported by GIGABYTE components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesGIGABYTE = { "GIGABYTE", "Giga-Byte" };

    /// <summary>Name variants reported by ASUSTeK components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesASUSTeK = { "ASUSTeK", "ASUS" };

    /// <summary>Name variants reported by Micro-Star components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesMicroStar = { "Micro-Star", "MSI" };

    /// <summary>Name variants reported by ASRock components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesASRock = { "ASRock" };

    /// <summary>Name variants reported by Dell components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesDell = { "Dell", "Alienware" };

    /// <summary>Name variants reported by HP components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesHP = { "HP", "Hewlett-Packard", "HPE" };

    /// <summary>Name variants reported by LENOVO components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesLENOVO = { "LENOVO", "ThinkPad", "IdeaPad" };

    /// <summary>Name variants reported by Acer components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesAcer = { "Acer", "Predator" };

    /// <summary>Name variants reported by Microsoft components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesMicrosoft = { "Microsoft", "Surface" };

    /// <summary>Name variants reported by Samsung components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesSamsung = { "Samsung" };

    /// <summary>Name variants reported by Kingston components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesKingston = { "Kingston" };

    /// <summary>Name variants reported by Crucial components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesCrucial = { "Crucial", "Micron" };

    /// <summary>Name variants reported by WDC components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesWDC = { "WDC", "Western Digital", "SanDisk" };

    /// <summary>Name variants reported by Seagate components (SMBIOS and driver data).</summary>
    private static readonly string[] VendorAliasesSeagate = { "Seagate", "ST" };

    private readonly IReadOnlyList<IManufacturerAdapter> _adapters;

    public ManufacturerResolver(IClock clock)
    {
        _adapters = new List<IManufacturerAdapter>
        {
            new AmdAdapter(clock),
            new NvidiaAdapter(clock),
            new IntelAdapter(clock),
            new BoardVendorAdapter(clock, "gigabyte", "Vendor_GIGABYTE", VendorAliasesGIGABYTE, ManufacturerSources.Gigabyte),
            new BoardVendorAdapter(clock, "asus", "Vendor_ASUS", VendorAliasesASUSTeK, ManufacturerSources.Asus),
            new BoardVendorAdapter(clock, "msi", "Vendor_MSI", VendorAliasesMicroStar, ManufacturerSources.Msi),
            new BoardVendorAdapter(clock, "asrock", "Vendor_ASRock", VendorAliasesASRock, ManufacturerSources.Asrock),
            new OemVendorAdapter(clock, "dell", "Vendor_Dell", VendorAliasesDell, ManufacturerSources.Dell),
            new OemVendorAdapter(clock, "hp", "Vendor_HP", VendorAliasesHP, ManufacturerSources.Hp),
            new OemVendorAdapter(clock, "lenovo", "Vendor_Lenovo", VendorAliasesLENOVO, ManufacturerSources.Lenovo),
            new OemVendorAdapter(clock, "acer", "Vendor_Acer", VendorAliasesAcer, ManufacturerSources.Acer),
            new OemVendorAdapter(clock, "microsoft", "Vendor_Microsoft", VendorAliasesMicrosoft, ManufacturerSources.Microsoft),
            new StorageVendorAdapter(clock, "samsung", "Vendor_Samsung", VendorAliasesSamsung, ManufacturerSources.Samsung),
            new StorageVendorAdapter(clock, "kingston", "Vendor_Kingston", VendorAliasesKingston, ManufacturerSources.Kingston),
            new StorageVendorAdapter(clock, "crucial", "Vendor_Crucial", VendorAliasesCrucial, ManufacturerSources.Crucial),
            new StorageVendorAdapter(clock, "westerndigital", "Vendor_WesternDigital", VendorAliasesWDC, ManufacturerSources.WesternDigital),
            new StorageVendorAdapter(clock, "seagate", "Vendor_Seagate", VendorAliasesSeagate, ManufacturerSources.Seagate),
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
