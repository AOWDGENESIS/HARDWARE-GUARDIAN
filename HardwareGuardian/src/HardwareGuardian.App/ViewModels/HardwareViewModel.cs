using System.Collections.ObjectModel;
using System.Globalization;
using HardwareGuardian.App.Mvvm;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.App.ViewModels;

/// <summary>
/// Hardware inventory page. Every row carries the origin of its values, and a value that could not
/// be read is shown as UNKNOWN with the reason - never as an empty field or a zero.
/// </summary>
public sealed class HardwareViewModel : ViewModelBase
{
    private SystemSnapshot? _snapshot;
    private string _summary = string.Empty;
    private string _filter = string.Empty;

    public HardwareViewModel(ILocalizer localizer)
        : base(localizer)
    {
    }

    public BulkObservableCollection<ComponentRow> Components { get; } = new();

    public BulkObservableCollection<DetailRow> Details { get; } = new();

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Rebuild();
            }
        }
    }

    public void Load(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        Summary = L("Dashboard_ComponentCount", snapshot.Components.Count);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_snapshot is null)
        {
            return;
        }

        var rows = _snapshot.Components
            .OrderBy(c => c.Category)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name.Display, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ComponentRow(
                L(c.CategoryKey),
                c.Name.Display,
                c.Manufacturer.Display,
                c.Model.Display,
                c.Status.ToString(),
                c.Status,
                c.Name.Origin.Token(),
                c.Driver is null ? "—" : c.Driver.Version.Display,
                c.Driver?.SignatureVerification.ToString() ?? "—",
                c.DeviceInstanceId.IsKnown ? c.DeviceInstanceId.Display : string.Empty));

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            var needle = Filter.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                r.Manufacturer.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                r.Category.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        Components.Reset(rows);
        Details.Reset(BuildDetails(_snapshot));
    }

    private IEnumerable<DetailRow> BuildDetails(SystemSnapshot snapshot)
    {
        var rows = new List<DetailRow>
        {
            new(L("Report_Section_Overall"), snapshot.Windows.ProductName.Display, snapshot.Windows.ProductName.Origin.Token()),
            new(L("Windows_Check_UpdateStatus"), snapshot.Windows.DisplayVersion.Display, snapshot.Windows.DisplayVersion.Origin.Token()),
            new(L("Component_Bios"), $"{snapshot.Bios.Version.Display} ({snapshot.Bios.ReleaseDate.Display})", snapshot.Bios.Version.Origin.Token()),
        };

        rows.AddRange(snapshot.Processors.Select(p => new DetailRow(
            $"{L("Component_Cpu")} · {p.Name.Display}",
            $"{p.Cores.Display} C / {p.LogicalProcessors.Display} T · {p.MaxClockMhz.Display} MHz · {L("Report_Source")}: {p.Name.Origin.Token()}",
            p.Name.Origin.Token())));

        rows.Add(new DetailRow(
            $"{L("Component_Mainboard")} · {snapshot.Motherboard.Product.Display}",
            $"{snapshot.Motherboard.Manufacturer.Display} · {L("Report_Revision")}: {snapshot.Motherboard.SmbiosBoardRevision.Display} · {L("Component_Bios")}: {snapshot.Bios.Version.Display}",
            snapshot.Motherboard.Product.Origin.Token()));

        rows.Add(new DetailRow(
            L("Component_Memory"),
            string.Format(
                CultureInfo.CurrentCulture,
                "{0} GB · {1}",
                snapshot.Memory.TotalPhysicalBytes.HasValue ? (snapshot.Memory.TotalPhysicalBytes.Value!.Value / 1024d / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) : "UNKNOWN",
                string.Join(
                    ", ",
                    snapshot.Memory.Modules.Select(m =>
                        $"{m.CapacityBytes.Display} @ {m.SpeedMhz.Display} MHz · {m.MemoryType.Display} · {m.FormFactor.Display}"))),
            snapshot.Memory.TotalPhysicalBytes.Origin.Token()));

        rows.AddRange(snapshot.Graphics.Select(g => new DetailRow(
            $"{L("Component_Graphics")} · {g.Name.Display}",
            $"{g.DriverVersion.Display} · {L("Report_DeviceInstanceId")}: {(g.PnpDeviceId.IsKnown ? g.PnpDeviceId.Display : "UNKNOWN")}",
            g.Name.Origin.Token())));

        rows.AddRange(snapshot.Storage.Select(s => new DetailRow(
            $"{L("Component_Storage")} · {s.Model.Display}",
            $"{s.SizeBytes.Display} · {s.BusType.Display} · {L("Sensor_DiskTemperature")}: {(s.TemperatureCelsius.HasValue ? s.TemperatureCelsius.Value!.Value.ToString("0.#", CultureInfo.CurrentCulture) + " °C" : "UNKNOWN (" + (s.TemperatureCelsius.UnknownReason ?? "n/a") + ")")}",
            s.Model.Origin.Token())));

        rows.AddRange(snapshot.Network.Select(n => new DetailRow(
            $"{L("Component_Network")} · {n.Name.Display}",
            $"{(n.MacAddress.IsKnown ? n.MacAddress.Display : "MAC UNKNOWN")} · {n.AdapterType.Display}",
            n.Name.Origin.Token())));

        if (snapshot.Battery is { } battery)
        {
            rows.Add(new DetailRow(
                L("Component_Battery"),
                $"{battery.Name.Display} · {battery.DesignCapacityMwh.Display} mWh",
                battery.Name.Origin.Token()));
        }

        return rows;
    }

    protected override void OnLanguageChangedCore() => Rebuild();

    protected override void DisposeCore()
    {
        Components.Clear();
        Details.Clear();
    }

    /// <summary>One component row of the inventory.</summary>
    public sealed record ComponentRow(
        string Category,
        string Name,
        string Manufacturer,
        string Model,
        string StatusText,
        Core.HealthStatus Status,
        string Origin,
        string DriverVersion,
        string Signature,
        string DeviceInstanceId);

    /// <summary>One additional inventory line (processor, memory, storage, ...).</summary>
    public sealed record DetailRow(string Title, string Value, string Origin);
}
