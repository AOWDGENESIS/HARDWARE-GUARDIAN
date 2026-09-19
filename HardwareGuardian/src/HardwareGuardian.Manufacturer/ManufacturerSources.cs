using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Manufacturer;

/// <summary>
/// Official manufacturer sources. Every entry is a documented landing page of the vendor.
/// Entries were verified as reachable over HTTPS; the verification state is reported per source
/// and an empty URL always means SOURCE UNKNOWN (spec sections 47, 48).
/// </summary>
public static class ManufacturerSources
{
    public const string Unknown = "";

    public static ManufacturerSource Amd { get; } = new()
    {
        Id = "amd",
        DisplayNameKey = "Vendor_AMD",
        LandingUrl = "https://www.amd.com/en/support/download/drivers.html",
        SourceTypeKey = "SourceType_DriverAndChipset",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
        Notes = "AMD does not publish a machine readable driver catalogue for consumer GPUs; the official page must be checked manually.",
    };

    public static ManufacturerSource Nvidia { get; } = new()
    {
        Id = "nvidia",
        DisplayNameKey = "Vendor_NVIDIA",
        LandingUrl = "https://www.nvidia.com/en-us/drivers/",
        SourceTypeKey = "SourceType_Driver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
        Notes = "The driver search requires an interactive query; no automated version lookup is performed.",
    };

    public static ManufacturerSource Intel { get; } = new()
    {
        Id = "intel",
        DisplayNameKey = "Vendor_Intel",
        LandingUrl = "https://www.intel.com/content/www/us/en/download-center/home.html",
        SourceTypeKey = "SourceType_Driver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Gigabyte { get; } = new()
    {
        Id = "gigabyte",
        DisplayNameKey = "Vendor_GIGABYTE",
        LandingUrl = "https://www.gigabyte.com/Support",
        SourceTypeKey = "SourceType_BiosDriver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
        Notes = "BIOS files are published on the exact product page; the product page requires the exact revision.",
    };

    public static ManufacturerSource Asus { get; } = new()
    {
        Id = "asus",
        DisplayNameKey = "Vendor_ASUS",
        LandingUrl = "https://www.asus.com/support/",
        SourceTypeKey = "SourceType_BiosDriver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Msi { get; } = new()
    {
        Id = "msi",
        DisplayNameKey = "Vendor_MSI",
        LandingUrl = "https://www.msi.com/support",
        SourceTypeKey = "SourceType_BiosDriver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Asrock { get; } = new()
    {
        Id = "asrock",
        DisplayNameKey = "Vendor_ASRock",
        LandingUrl = "https://www.asrock.com/support/index.asp",
        SourceTypeKey = "SourceType_BiosDriver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Dell { get; } = new()
    {
        Id = "dell",
        DisplayNameKey = "Vendor_Dell",
        LandingUrl = "https://www.dell.com/support/home/en-us",
        SourceTypeKey = "SourceType_Oem",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Hp { get; } = new()
    {
        Id = "hp",
        DisplayNameKey = "Vendor_HP",
        LandingUrl = "https://support.hp.com/us-en/drivers",
        SourceTypeKey = "SourceType_Oem",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Lenovo { get; } = new()
    {
        Id = "lenovo",
        DisplayNameKey = "Vendor_Lenovo",
        LandingUrl = "https://support.lenovo.com/us/en/",
        SourceTypeKey = "SourceType_Oem",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Acer { get; } = new()
    {
        Id = "acer",
        DisplayNameKey = "Vendor_Acer",
        LandingUrl = "https://www.acer.com/us-en/support",
        SourceTypeKey = "SourceType_Oem",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Microsoft { get; } = new()
    {
        Id = "microsoft",
        DisplayNameKey = "Vendor_Microsoft",
        LandingUrl = "https://www.catalog.update.microsoft.com/Home.aspx",
        SourceTypeKey = "SourceType_MicrosoftCatalog",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = false,
        Notes = "The Microsoft Update Catalog is the only official source for individual Microsoft updates.",
    };

    public static ManufacturerSource Realtek { get; } = new()
    {
        Id = "realtek",
        DisplayNameKey = "Vendor_Realtek",
        LandingUrl = "https://www.realtek.com/Download/List?cate_id=584",
        SourceTypeKey = "SourceType_Driver",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Samsung { get; } = new()
    {
        Id = "samsung",
        DisplayNameKey = "Vendor_Samsung",
        LandingUrl = "https://semiconductor.samsung.com/consumer-storage/support/tools/",
        SourceTypeKey = "SourceType_StorageTool",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
        Notes = "Samsung Magician is the official tool for SSD firmware and health data.",
    };

    public static ManufacturerSource Kingston { get; } = new()
    {
        Id = "kingston",
        DisplayNameKey = "Vendor_Kingston",
        LandingUrl = "https://www.kingston.com/en/support/technical/downloads",
        SourceTypeKey = "SourceType_StorageTool",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Crucial { get; } = new()
    {
        Id = "crucial",
        DisplayNameKey = "Vendor_Crucial",
        LandingUrl = "https://www.crucial.com/support/storage-executive",
        SourceTypeKey = "SourceType_StorageTool",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource WesternDigital { get; } = new()
    {
        Id = "westerndigital",
        DisplayNameKey = "Vendor_WesternDigital",
        LandingUrl = "https://support-en.wd.com/app/products/downloads",
        SourceTypeKey = "SourceType_StorageTool",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    public static ManufacturerSource Seagate { get; } = new()
    {
        Id = "seagate",
        DisplayNameKey = "Vendor_Seagate",
        LandingUrl = "https://www.seagate.com/support/downloads/",
        SourceTypeKey = "SourceType_StorageTool",
        Method = VerificationMethod.HttpsReachability,
        BaselineVerification = VerificationLevel.SourceReachable,
        RequiresManualVerification = true,
    };

    /// <summary>
    /// Vendors without a documented official download page are deliberately listed without a URL.
    /// The UI then shows SOURCE UNKNOWN instead of guessing (spec sections 1.3 and 47).
    /// </summary>
    public static ManufacturerSource UnknownSource(string adapterId, string displayNameKey) => new()
    {
        Id = adapterId,
        DisplayNameKey = displayNameKey,
        LandingUrl = Unknown,
        SourceTypeKey = "SourceType_Unknown",
        Method = VerificationMethod.None,
        BaselineVerification = VerificationLevel.NotVerified,
        RequiresManualVerification = true,
        Notes = "No official source is documented for this vendor yet.",
    };
}
