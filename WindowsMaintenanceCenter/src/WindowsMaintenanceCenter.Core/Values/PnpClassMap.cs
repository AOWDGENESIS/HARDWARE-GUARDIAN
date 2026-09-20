using WindowsMaintenanceCenter.Core;

namespace WindowsMaintenanceCenter.Core.Values;

/// <summary>
/// Maps the PnP setup class of a device (<c>Win32_PnPEntity.PNPClass</c>, the same string that the
/// driver node reports as <c>DeviceClass</c>) to the category the application uses.
///
/// Rules (spec sections 1.3 and 44):
/// - only documented setup classes are mapped; a class that is not in this table becomes
///   <see cref="ComponentCategory.Unknown"/>, never a guessed category,
/// - the table only says which group a device belongs to. It never claims that the device works.
///
/// The previous implementation ended with <c>_ =&gt; ComponentCategory.Pci</c>, so every unlisted
/// class - storage enclosures, thermal zones, volume devices - was reported as a PCI device, a bus
/// type that was never measured.
/// </summary>
public static class PnpClassMap
{
    /// <summary>
    /// Setup classes that carry measurement data are listed here too, so an unlisted class is
    /// visibly different from a known one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ComponentCategory> Classes =
        new Dictionary<string, ComponentCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Processor and platform
            ["PROCESSOR"] = ComponentCategory.Cpu,
            ["COMPUTER"] = ComponentCategory.System,
            ["SYSTEM"] = ComponentCategory.System,
            ["FIRMWARE"] = ComponentCategory.Firmware,

            // Graphics and display
            ["DISPLAY"] = ComponentCategory.Graphics,
            ["MONITOR"] = ComponentCategory.Monitor,

            // Storage stack
            ["DISKDRIVE"] = ComponentCategory.Storage,
            ["HDC"] = ComponentCategory.Storage,
            ["SCSIADAPTER"] = ComponentCategory.Storage,
            ["CDROM"] = ComponentCategory.Storage,
            ["VOLUME"] = ComponentCategory.Storage,
            ["VOLUMESNAPSHOT"] = ComponentCategory.Storage,

            // Communication
            ["NET"] = ComponentCategory.Network,
            ["MODEM"] = ComponentCategory.Network,
            ["BLUETOOTH"] = ComponentCategory.Network,

            // Peripherals
            ["MEDIA"] = ComponentCategory.Audio,
            ["USB"] = ComponentCategory.Usb,
            ["PRINTER"] = ComponentCategory.Printer,
            ["PRINTQUEUE"] = ComponentCategory.Printer,
            ["BATTERY"] = ComponentCategory.Battery,

            // Sensors: the Thermal setup class covers the ACPI thermal zones and their sensors.
            ["THERMAL"] = ComponentCategory.Sensor,
        };

    /// <summary>Categories a device with this setup class belongs to.</summary>
    public static ComponentCategory Map(string? pnpClass)
    {
        if (string.IsNullOrWhiteSpace(pnpClass))
        {
            return ComponentCategory.Unknown;
        }

        return Classes.TryGetValue(pnpClass.Trim(), out var category)
            ? category
            : ComponentCategory.Unknown;
    }

    /// <summary>
    /// True when the class is documented in this table. The caller can then explain the empty
    /// category as "class not listed" instead of leaving the user with a bare "Unknown".
    /// </summary>
    public static bool IsKnown(string? pnpClass) =>
        !string.IsNullOrWhiteSpace(pnpClass) && Classes.ContainsKey(pnpClass.Trim());

    /// <summary>The setup classes this table knows, for diagnostics and the protocol.</summary>
    public static IReadOnlyCollection<string> KnownClasses => (IReadOnlyCollection<string>)Classes.Keys;
}
