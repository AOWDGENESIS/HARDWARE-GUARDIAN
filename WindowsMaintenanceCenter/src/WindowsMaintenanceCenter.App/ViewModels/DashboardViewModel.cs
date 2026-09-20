using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using WindowsMaintenanceCenter.App.Mvvm;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.App.ViewModels;

/// <summary>
/// Overview page: overall state, findings, modules and the report actions.
/// It shows what the last scan really produced - never a placeholder result.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly IProblemRegistry _problems;
    private readonly IReportGenerator _reports;
    private readonly ILiveProtocol _protocol;
    private readonly ISettingsService _settings;
    private readonly IAuditLog _audit;

    private SystemSnapshot? _snapshot;
    private string _overallSummary = string.Empty;
    private HealthStatus _overallStatus = HealthStatus.Unknown;
    private string _lastScanText = string.Empty;
    private string? _message;

    public DashboardViewModel(
        ILocalizer localizer,
        IProblemRegistry problems,
        IReportGenerator reports,
        ILiveProtocol protocol,
        ISettingsService settings,
        IAuditLog audit)
        : base(localizer)
    {
        _problems = problems;
        _reports = reports;
        _protocol = protocol;
        _settings = settings;
        _audit = audit;

        ScanCommand = new RelayCommand(() => ScanRequested?.Invoke(this, EventArgs.Empty));
        ReportJsonCommand = new AsyncRelayCommand(() => GenerateAsync(ReportFormat.Json));
        ReportTextCommand = new AsyncRelayCommand(() => GenerateAsync(ReportFormat.Text));
        ReportHtmlCommand = new AsyncRelayCommand(() => GenerateAsync(ReportFormat.Html));

        _problems.Cleared += (_, _) => RefreshProblems();
        _problems.ProblemRegistered += (_, _) => RefreshProblems();
    }

    /// <summary>Raised when the user asks for a scan; the window view model owns the running scan.</summary>
    public event EventHandler? ScanRequested;

    public BulkObservableCollection<Problem> Problems { get; } = new();

    public BulkObservableCollection<SensorRow> Sensors { get; } = new();

    public BulkObservableCollection<ModuleRow> Modules { get; } = new();

    public RelayCommand ScanCommand { get; }

    public AsyncRelayCommand ReportJsonCommand { get; }

    public AsyncRelayCommand ReportTextCommand { get; }

    public AsyncRelayCommand ReportHtmlCommand { get; }

    public string OverallSummary
    {
        get => _overallSummary;
        private set => SetProperty(ref _overallSummary, value);
    }

    public HealthStatus OverallStatus
    {
        get => _overallStatus;
        private set => SetProperty(ref _overallStatus, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => SetProperty(ref _lastScanText, value);
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string CountsLine
    {
        get
        {
            var counts = _problems.Counts;
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1} · {2}: {3} · {4}: {5} · {6}: {7}",
                L("Severity_Critical"), counts.Critical,
                L("Severity_Error"), counts.Errors,
                L("Severity_Warning"), counts.Warnings,
                L("Severity_Blocked"), counts.Blocked);
        }
    }

    public string ComponentCountText => _snapshot is null
        ? L("Text_Missing")
        : L("Dashboard_ComponentCount", _snapshot.Components.Count);

    public string SimulationNotice => L("Report_SimulationWarning");

    public bool IsSimulation => _snapshot?.IsSimulation ?? App.IsSimulationRequested;

    /// <summary>Takes over the result of a finished scan.</summary>
    public void Load(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;

        OverallStatus = snapshot.OverallStatus;
        OverallSummary = L(snapshot.OverallSummary);
        LastScanText = snapshot.CapturedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Message = null;

        Sensors.Reset(snapshot.SensorSnapshot.Select(r => new SensorRow(
            L(r.NameKey),
            r.Value.HasValue ? $"{r.Value.Value!.Value.ToString("0.##", CultureInfo.CurrentCulture)} {r.Unit}" : "UNKNOWN",
            r.Value.UnknownReason ?? string.Empty,
            r.Quality.ToString(),
            L(r.MeasurementPointKey),
            r.Origin.Token())));

        Modules.Reset(snapshot.Modules.Select(m => new ModuleRow(
            L(m.DisplayNameKey),
            m.Status.ToString(),
            m.WasSkipped ? L(m.SkipReason, "Module_Unknown") : L("Module_System"),
            m.ChecksExecuted.ToString(CultureInfo.CurrentCulture),
            m.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture),
            m.Evidence.FirstOrDefault() ?? string.Empty)));

        RefreshProblems();
        OnPropertyChanged(nameof(ComponentCountText));
        OnPropertyChanged(nameof(IsSimulation));
    }

    private void RefreshProblems()
    {
        Problems.Reset(_problems.All.OrderByDescending(p => p.Severity).ThenBy(p => p.Id, StringComparer.Ordinal));
        OnPropertyChanged(nameof(CountsLine));
    }

    private async Task GenerateAsync(ReportFormat format)
    {
        if (_snapshot is null)
        {
            Message = L("Report_NoProblems");
            return;
        }

        IsBusy = true;
        try
        {
            var request = new ReportRequest
            {
                Snapshot = _snapshot,
                Sensors = _snapshot.SensorSnapshot,
                Audit = _audit.Recent(200),
            };

            var options = new ReportOptions
            {
                MaskSerialNumbers = _settings.Current.MaskSerialNumbersInReports,
                MaskUserName = _settings.Current.MaskUserNameInReports,
                IncludeEvidence = _settings.Current.IncludeEvidenceInReports,
                OutputDirectory = _settings.Current.ReportDirectory,
            };

            var artifact = await _reports.GenerateAsync(request, format, options, CancellationToken.None).ConfigureAwait(true);
            Message = L(artifact.Summary) + " " + artifact.FilePath;
            _protocol.Success("SYS", LocalizedText.Of("Report_Created", format.ToString(), artifact.SizeBytes / 1024d));
        }
        catch (OperationBlockedException blocked)
        {
            // Fail closed: the reason is shown, no file is claimed.
            Message = $"{blocked.ReasonCode}: {L(blocked.Reason)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override void OnLanguageChangedCore()
    {
        if (_snapshot is null)
        {
            return;
        }

        // Re-derive every produced string from the same snapshot, so nothing keeps the old language.
        Load(_snapshot);
    }

    protected override void DisposeCore()
    {
        Problems.Clear();
        Sensors.Clear();
        Modules.Clear();
    }

    /// <summary>One sensor reading, already resolved into display text including its measurement point.</summary>
    public sealed record SensorRow(string Name, string Value, string UnknownReason, string Quality, string MeasurementPoint, string Origin);

    /// <summary>One diagnostic module result.</summary>
    public sealed record ModuleRow(string Name, string Status, string Note, string Checks, string Seconds, string Evidence);
}
