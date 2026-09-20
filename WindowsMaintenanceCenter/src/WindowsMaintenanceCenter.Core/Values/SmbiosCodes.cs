namespace WindowsMaintenanceCenter.Core.Values;

/// <summary>
/// Documented SMBIOS/CIM code tables, used to name a raw firmware code.
///
/// Why this exists: WMI reports several hardware properties as bare numbers
/// (<c>Win32_PhysicalMemory.SMBIOSMemoryType</c> = 26, <c>Win32_SystemEnclosure.ChassisTypes</c> = 3).
/// Passing those numbers on unchanged would be unreadable, and translating them from memory would
/// risk a wrong label - which is exactly the fabricated data this application must not produce.
/// Therefore:
///
/// * the code itself is always part of the returned text, so every statement stays verifiable,
/// * only values that are documented in the specifications are translated:
///   - memory device type: DMTF DSP0134, section 7.18.2 (Memory Device - Type)
///   - memory form factor and chassis type: the CIM/WMI enumerations of
///     <c>CIM_PhysicalMemory.FormFactor</c> and <c>Win32_SystemEnclosure.ChassisTypes</c>
/// * a value outside those tables is reported as "SMBIOS code N" without a guessed name.
///
/// The notorious PowerShell lookup table that circulates on the internet is deliberately not used:
/// it labels 20/21/22 as DDR/DDR2/DDR2 FB-DIMM, while the specification has those values at
/// 0x12/0x13/0x14 (18/19/20). Following it would have produced a wrong memory type on every machine
/// from DDR2 onwards. <c>SmbiosCodesTests</c> pins the documented anchors.
/// </summary>
public static class SmbiosCodes
{
    /// <summary>Name of a memory device type code (<c>Win32_PhysicalMemory.SMBIOSMemoryType</c>).</summary>
    public static string MemoryType(uint code) => code switch
    {
        0x01 => "Other",
        0x02 => "Unknown",
        0x03 => "DRAM",
        0x04 => "EDRAM",
        0x05 => "VRAM",
        0x06 => "SRAM",
        0x07 => "RAM",
        0x08 => "ROM",
        0x09 => "Flash",
        0x0A => "EEPROM",
        0x0B => "FEPROM",
        0x0C => "EPROM",
        0x0D => "CDRAM",
        0x0E => "3DRAM",
        0x0F => "SDRAM",
        0x10 => "SGRAM",
        0x11 => "RDRAM",
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x19 => "FBD2",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x1F => "Logical non-volatile device",
        0x22 => "DDR5",
        _ => Unknown(code),
    };

    /// <summary>Name of a memory module form factor (<c>Win32_PhysicalMemory.FormFactor</c>).</summary>
    public static string MemoryFormFactor(uint code) => code switch
    {
        0 => "Unknown",
        1 => "Other",
        2 => "SIP",
        3 => "DIP",
        4 => "ZIP",
        5 => "SOJ",
        6 => "Proprietary",
        7 => "SIMM",
        8 => "DIMM",
        9 => "TSOP",
        10 => "PGA",
        11 => "RIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        _ => Unknown(code),
    };

    /// <summary>Name of a system enclosure type (<c>Win32_SystemEnclosure.ChassisTypes</c>).</summary>
    public static string ChassisType(uint code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Desktop",
        4 => "Low profile desktop",
        5 => "Pizza box",
        6 => "Mini tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Hand held",
        12 => "Docking station",
        13 => "All in one",
        14 => "Sub notebook",
        15 => "Space-saving",
        16 => "Lunch box",
        17 => "Main system chassis",
        18 => "Expansion chassis",
        19 => "Sub chassis",
        20 => "Bus expansion chassis",
        21 => "Peripheral chassis",
        22 => "Storage chassis",
        23 => "Rack mount chassis",
        24 => "Sealed-case PC",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        33 => "IoT gateway",
        34 => "Embedded PC",
        35 => "Mini PC",
        36 => "Stick PC",
        _ => Unknown(code),
    };

    /// <summary>
    /// Formats the name together with the code it came from: <c>DDR4 (SMBIOS code 26)</c>.
    /// The code stays visible so a reader can check the statement against the specification.
    /// </summary>
    public static string Describe(string name, uint code) => $"{name} (SMBIOS code {code})";

    private static string Unknown(uint code) => $"code {code} (not in the documented table)";
}
