using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Serialization;

namespace WindowsMaintenanceCenter.Reporting;

/// <summary>
/// Report generation (spec sections 56 and 57). Reports always contain the provenance of every
/// value and the evidence behind every finding; masking is applied as configured.
///
/// PDF is deliberately NOT implemented: a "PDF" that is really a renamed text file would be a fake.
/// The format table therefore reports HTML as the printable format and PDF as unsupported with a
/// reason that the UI shows. HTML and TXT printing already covers the documented use case.
/// </summary>
public sealed class ReportGenerator : IReportGenerator
{
    private static readonly ReportFormat[] Formats = { ReportFormat.Html, ReportFormat.Text, ReportFormat.Json };

    private readonly IPathProvider _paths;
    private readonly IHashService _hashes;
    private readonly ILocalizer _localizer;
    private readonly IBuildInfoProvider? _buildInfo;
    private readonly IEnvironmentProbe _environment;
    private readonly IClock _clock;

    public ReportGenerator(
        IPathProvider paths,
        IHashService hashes,
        ILocalizer localizer,
        IEnvironmentProbe environment,
        IClock clock,
        IBuildInfoProvider? buildInfo = null)
    {
        _paths = paths;
        _hashes = hashes;
        _localizer = localizer;
        _environment = environment;
        _clock = clock;
        _buildInfo = buildInfo;
    }

    public IReadOnlyList<ReportFormat> SupportedFormats => Formats;

    public async Task<ReportArtifact> GenerateAsync(ReportRequest request, ReportFormat format, ReportOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!Formats.Contains(format))
        {
            // Fail closed: no file is produced, and the caller gets a precise reason.
            throw new OperationBlockedException(
                BlockReasons.UnsupportedPlatform,
                LocalizedText.Of("Report_Blocked_FormatUnsupported", format.ToString()),
                $"Supported formats: {string.Join(", ", Formats)}");
        }

        var directory = ResolveDirectory(options);
        _paths.EnsureDirectory(directory);

        var fileName = BuildFileName(request, format, options);
        var path = Path.Combine(directory, fileName);
        var content = BuildContent(request, format, options);

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: format == ReportFormat.Text), cancellationToken).ConfigureAwait(false);

        var hash = await _hashes.ComputeFileHashAsync(path, "SHA256", null, cancellationToken).ConfigureAwait(false);
        var size = new FileInfo(path).Length;

        return new ReportArtifact
        {
            FilePath = path,
            Format = format,
            SizeBytes = size,
            CreatedAt = _clock.Now,
            Hash = hash,
            Summary = LocalizedText.Of(
                request.IsSecurityReport ? "Report_Created_Security" : "Report_Created",
                format.ToString(),
                size / 1024d),
        };
    }

    private string BuildContent(ReportRequest request, ReportFormat format, ReportOptions options) => format switch
    {
        ReportFormat.Json => BuildJson(request, options),
        ReportFormat.Html => BuildHtml(request, options),
        _ => BuildText(request, options),
    };

    private string ResolveDirectory(ReportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            return options.OutputDirectory!;
        }

        return _paths.ReportDirectory;
    }

    private string BuildFileName(ReportRequest request, ReportFormat format, ReportOptions options)
    {
        var kind = request.IsSecurityReport ? "security-report" : "system-report";
        var hint = Sanitise(options.FileNameHint ?? kind);
        var extension = format switch
        {
            ReportFormat.Json => "json",
            ReportFormat.Html => "html",
            _ => "txt",
        };

        return $"{hint}-{_clock.Now:yyyyMMdd-HHmmss}.{extension}";
    }

    // ---------------------------------------------------------------------------------------------
    // JSON: machine readable, every value keeps its origin token
    // ---------------------------------------------------------------------------------------------
    private string BuildJson(ReportRequest request, ReportOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["reportType"] = request.IsSecurityReport ? "security" : "system",
            ["generatedAt"] = _clock.Now.ToString("o", CultureInfo.InvariantCulture),
            ["language"] = _localizer.Culture.Name,
            ["application"] = new Dictionary<string, object?>
            {
                ["version"] = _buildInfo?.Get().Version ?? "unknown",
                ["buildDate"] = _buildInfo?.Get().BuildDate ?? "unspecified",
                ["masking"] = new Dictionary<string, bool>
                {
                    ["serialNumbers"] = options.MaskSerialNumbers,
                    ["userName"] = options.MaskUserName,
                },
            },
            ["environment"] = new Dictionary<string, object?>
            {
                ["isWindows"] = _environment.IsWindows,
                ["osDescription"] = _environment.OsDescription,
                ["osArchitecture"] = _environment.OsArchitecture,
                ["privilege"] = _environment.Privilege.ToString(),
                ["machineName"] = options.MaskUserName && !_environment.IsWindows ? "unavailable" : "present (masked in this file)",
            },
        };

        if (request.Snapshot is { } snapshot)
        {
            payload["snapshot"] = new Dictionary<string, object?>
            {
                ["id"] = snapshot.Id,
                ["capturedAt"] = snapshot.CapturedAt.ToString("o", CultureInfo.InvariantCulture),
                ["isSimulation"] = snapshot.IsSimulation,
                ["overallStatus"] = snapshot.OverallStatus.ToString(),
                ["overallSummary"] = _localizer.Resolve(snapshot.OverallSummary),
                // An incomplete inventory has to be visible in the file: without this a report
                // would show empty lists and look like "nothing found".
                ["inventoryFailedReads"] = snapshot.InventoryFailedReads,
                ["inventoryNotes"] = snapshot.InventoryNotes,
                ["hardware"] = BuildHardwareJson(snapshot, options),
                ["problems"] = snapshot.Problems.Select(p => new Dictionary<string, object?>
                {
                    ["id"] = p.Id,
                    ["category"] = p.Category.ToString(),
                    ["severity"] = p.Severity.ToString(),
                    ["status"] = p.Status.ToString(),
                    ["title"] = _localizer.Resolve(p.Title),
                    ["description"] = _localizer.Resolve(p.Description),
                    ["impact"] = _localizer.Resolve(p.Impact),
                    ["recommendedAction"] = _localizer.Resolve(p.RecommendedAction),
                    ["evidence"] = options.IncludeEvidence ? p.Evidence : "(evidence omitted by settings)",
                    ["detectedAt"] = p.DetectedAt.ToString("o", CultureInfo.InvariantCulture),
                    ["componentId"] = p.ComponentId,
                    ["references"] = p.References,
                    ["blockedOperations"] = p.BlockedOperations.Select(b => new Dictionary<string, object?>
                    {
                        ["operationId"] = b.OperationId,
                        ["reasonCode"] = b.ReasonCode,
                        ["reason"] = _localizer.Resolve(b.Reason),
                        ["detail"] = b.Detail,
                        ["blockedAt"] = b.BlockedAt.ToString("o", CultureInfo.InvariantCulture),
                    }).ToList(),
                }).ToList(),
                ["components"] = snapshot.Components.Select(c => new Dictionary<string, object?>
                {
                    ["id"] = c.Id,
                    ["category"] = c.Category.ToString(),
                    ["name"] = c.Name.Display,
                    ["manufacturer"] = c.Manufacturer.Display,
                    ["model"] = c.Model.Display,
                    ["status"] = c.Status.ToString(),
                    ["origin"] = c.Name.Origin.Token(),
                    ["deviceInstanceId"] = c.DeviceInstanceId.IsKnown ? MaskSerial(c.DeviceInstanceId.Display, options) : "UNKNOWN",
                    ["driver"] = c.Driver is null ? null : new Dictionary<string, object?>
                    {
                        ["provider"] = c.Driver.Provider.Display,
                        ["version"] = c.Driver.Version.Display,
                        ["date"] = c.Driver.Date.Display,
                        ["signatureVerified"] = c.Driver.IsSignatureVerified,
                        ["verificationLevel"] = c.Driver.SignatureVerification.ToString(),
                        ["problemCode"] = c.Driver.ProblemCode.HasValue ? c.Driver.ProblemCode.Value!.Value : null,
                    },
                }).ToList(),
                ["modules"] = snapshot.Modules.Select(m => new Dictionary<string, object?>
                {
                    ["moduleId"] = m.ModuleId,
                    ["status"] = m.Status.ToString(),
                    ["checksExecuted"] = m.ChecksExecuted,
                    ["wasSkipped"] = m.WasSkipped,
                    ["skipReason"] = m.SkipReason is null ? null : _localizer.Resolve(m.SkipReason),
                    ["durationSeconds"] = Math.Round(m.Duration.TotalSeconds, 2),
                    ["evidence"] = options.IncludeEvidence ? m.Evidence : new List<string>(),
                }).ToList(),
                ["workload"] = new Dictionary<string, object?>
                {
                    ["profile"] = snapshot.Workload.Profile.ToString(),
                    ["summary"] = _localizer.Resolve(snapshot.Workload.Summary),
                    ["detected"] = snapshot.Workload.Detected.Select(d => new Dictionary<string, object?>
                    {
                        ["id"] = d.Id,
                        ["detected"] = d.Detected,
                        ["evidence"] = d.Evidence,
                        ["mustNotBeDisturbed"] = d.MustNotBeDisturbed,
                    }).ToList(),
                },
                ["sensors"] = request.Sensors.Count > 0
                    ? request.Sensors.Select(s => (object)new Dictionary<string, object?>
                    {
                        ["id"] = s.Id,
                        ["value"] = s.Value.HasValue ? s.Value.Value!.Value : null,
                        ["unit"] = s.Unit,
                        ["quality"] = s.Quality.ToString(),
                        ["measurementPoint"] = s.MeasurementPointKey,
                        ["origin"] = s.Origin.Token(),
                        ["unknownReason"] = s.Value.UnknownReason,
                    }).ToList()
                    : snapshot.SensorSnapshot.Select(s => (object)new Dictionary<string, object?>
                    {
                        ["id"] = s.Id,
                        ["value"] = s.Value.HasValue ? s.Value.Value!.Value : null,
                        ["unit"] = s.Unit,
                        ["quality"] = s.Quality.ToString(),
                        ["measurementPoint"] = s.MeasurementPointKey,
                        ["origin"] = s.Origin.Token(),
                        ["unknownReason"] = s.Value.UnknownReason,
                    }).ToList(),
            };
        }
        else
        {
            payload["snapshot"] = null;
            payload["note"] = "no snapshot was included in this report";
        }

        payload["updates"] = request.Updates.Select(u => new Dictionary<string, object?>
        {
            ["componentId"] = u.ComponentId,
            ["deviceName"] = u.DeviceName.Display,
            ["status"] = u.Status.ToString(),
            ["installed"] = u.Installed.Raw.Display,
            ["available"] = u.Available.Raw.Display,
            ["trust"] = u.Source.Trust.ToString(),
            ["verification"] = u.Source.Verification.ToString(),
            ["sourceUrl"] = u.Source.Url,
            ["freshness"] = u.Freshness.ToString(),
            ["reason"] = _localizer.Resolve(u.Reason),
            ["blockedReasonCode"] = u.BlockedReasonCode,
            ["canDownload"] = u.CanDownload,
            ["canInstall"] = u.CanInstall,
        }).ToList();

        payload["maintenance"] = request.Maintenance.Select(m => new Dictionary<string, object?>
        {
            ["planId"] = m.PlanId,
            ["mode"] = m.Mode.ToString(),
            ["startedAt"] = m.StartedAt.ToString("o", CultureInfo.InvariantCulture),
            ["completedAt"] = m.CompletedAt.ToString("o", CultureInfo.InvariantCulture),
            ["freedBytes"] = m.FreedBytes.HasValue ? m.FreedBytes.Value!.Value : null,
            ["summary"] = _localizer.Resolve(m.Summary),
            ["items"] = m.Items.Select(i => new Dictionary<string, object?>
            {
                ["itemId"] = i.ItemId,
                ["category"] = i.Category.ToString(),
                ["outcome"] = i.Outcome.ToString(),
                ["deletedFiles"] = i.DeletedFiles.HasValue ? i.DeletedFiles.Value!.Value : null,
                ["skippedFiles"] = i.SkippedFiles.HasValue ? i.SkippedFiles.Value!.Value : null,
                ["message"] = i.Message is null ? null : _localizer.Resolve(i.Message),
            }).ToList(),
        }).ToList();

        payload["history"] = request.History.Select(h => new Dictionary<string, object?>
        {
            ["id"] = h.Id,
            ["timestamp"] = h.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            ["kind"] = h.Kind,
            ["overallStatus"] = h.OverallStatus.ToString(),
            ["problemCount"] = h.ProblemCount,
            ["criticalCount"] = h.CriticalCount,
            ["warningCount"] = h.WarningCount,
            ["isSimulation"] = h.IsSimulation,
        }).ToList();

        payload["audit"] = options.IncludeEvidence
            ? request.Audit.Select(a => (object)new Dictionary<string, object?>
            {
                ["id"] = a.Id,
                ["timestamp"] = a.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                ["operation"] = a.Operation.ToString(),
                ["operationKey"] = a.OperationKey,
                ["category"] = a.Category.ToString(),
                ["componentId"] = a.ComponentId,
                ["result"] = a.Result.ToString(),
                ["oldState"] = a.OldState,
                ["newState"] = a.NewState,
                ["error"] = a.Error,
                ["approval"] = a.Approval is null ? null : new Dictionary<string, object?>
                {
                    ["requestId"] = a.Approval.RequestId,
                    ["decision"] = a.Approval.Decision.ToString(),
                    ["risk"] = a.Approval.Risk.ToString(),
                    ["wasRequired"] = a.Approval.WasRequired,
                },
                ["backup"] = a.Backup is null ? null : new Dictionary<string, object?>
                {
                    ["id"] = a.Backup.Id,
                    ["createdAt"] = a.Backup.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
                },
                ["evidence"] = a.Evidence,
            }).ToList()
            : new List<object>();

        if (request.Security is { } security)
        {
            payload["security"] = new Dictionary<string, object?>
            {
                ["hashes"] = security.Hashes.Select(h => new Dictionary<string, object?>
                {
                    ["path"] = h.Path,
                    ["algorithm"] = h.Algorithm,
                    ["hash"] = h.Hash,
                    ["succeeded"] = h.Succeeded,
                }).ToList(),
                ["signatures"] = security.Signatures.Select(s => new Dictionary<string, object?>
                {
                    ["path"] = s.Path,
                    ["isSigned"] = s.IsSigned,
                    ["isTrusted"] = s.IsTrusted,
                    ["verification"] = s.Verification.ToString(),
                    ["signer"] = s.Signer.Display,
                    ["detail"] = s.Detail,
                }).ToList(),
                ["sources"] = security.Sources.Select(s => new Dictionary<string, object?>
                {
                    ["url"] = s.Url,
                    ["reachable"] = s.Reachable,
                    ["verification"] = s.Verification.ToString(),
                    ["trust"] = s.Trust.ToString(),
                    ["httpStatusCode"] = s.HttpStatusCode,
                    ["errorCode"] = s.ErrorCode,
                }).ToList(),
                ["blockedOperations"] = security.BlockedOperations.Select(b => new Dictionary<string, object?>
                {
                    ["operationId"] = b.OperationId,
                    ["reasonCode"] = b.ReasonCode,
                    ["reason"] = _localizer.Resolve(b.Reason),
                }).ToList(),
                ["notes"] = security.Notes,
            };
        }

        return JsonSerializer.Serialize(payload, JsonOptions.Default);
    }

    // ---------------------------------------------------------------------------------------------
    // Plain text: the human readable variant, used by the audit text log as well
    // ---------------------------------------------------------------------------------------------
    private string BuildText(ReportRequest request, ReportOptions options)
    {
        var builder = new StringBuilder();
        var title = request.IsSecurityReport ? "Report_Title_Security" : "Report_Title_System";

        builder.AppendLine(_localizer.Resolve(LocalizedText.Of(title)));
        builder.AppendLine(new string('=', 78));
        builder.AppendLine($"{_localizer["Report_GeneratedAt"]}: {_clock.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"{_localizer["Report_Application"]}: {_buildInfo?.Get().Version ?? "unknown"} ({_buildInfo?.Get().Commit ?? "unknown"})");
        builder.AppendLine($"{_localizer["Report_Language"]}: {_localizer.Culture.Name}");
        builder.AppendLine();

        if (request.Snapshot is { } snapshot)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Overall")));
            builder.AppendLine(new string('-', 78));
            builder.AppendLine($"{_localizer["Report_OverallStatus"]}: {snapshot.OverallStatus}");
            builder.AppendLine($"{_localizer["Report_OverallSummary"]}: {_localizer.Resolve(snapshot.OverallSummary)}");
            builder.AppendLine($"{_localizer["Report_Simulation"]}: {(snapshot.IsSimulation ? _localizer["Report_Yes"] : _localizer["Report_No"])}");
            builder.AppendLine($"{_localizer["Report_SnapshotId"]}: {snapshot.Id} ({snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss})");
            if (snapshot.InventoryFailedReads > 0)
            {
                builder.AppendLine($"{_localizer["Report_FailedReads"]}: {snapshot.InventoryFailedReads}");
                foreach (var note in snapshot.InventoryNotes)
                {
                    builder.AppendLine($"    {note}");
                }
            }
            builder.AppendLine();

            AppendHardwareSection(builder, snapshot, options);

            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Problems")));
            builder.AppendLine(new string('-', 78));
            if (snapshot.Problems.Count == 0)
            {
                builder.AppendLine(_localizer["Report_NoProblems"]);
            }

            foreach (var problem in snapshot.Problems)
            {
                builder.AppendLine($"[{problem.Id}] {problem.Severity} · {problem.Status} · {_localizer.Resolve(problem.Title)}");
                builder.AppendLine($"    {_localizer.Resolve(problem.Description)}");
                builder.AppendLine($"    {_localizer["Report_Impact"]}: {_localizer.Resolve(problem.Impact)}");
                builder.AppendLine($"    {_localizer["Report_RecommendedAction"]}: {_localizer.Resolve(problem.RecommendedAction)}");
                if (options.IncludeEvidence && !string.IsNullOrWhiteSpace(problem.Evidence))
                {
                    builder.AppendLine($"    {_localizer["Report_Evidence"]}: {problem.Evidence}");
                }

                foreach (var blocked in problem.BlockedOperations)
                {
                    builder.AppendLine($"    {_localizer["Report_Blocked"]}: {blocked.ReasonCode} — {_localizer.Resolve(blocked.Reason)}");
                }

                builder.AppendLine();
            }

            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Components")));
            builder.AppendLine(new string('-', 78));
            foreach (var component in snapshot.Components.OrderBy(c => c.SortOrder).ThenBy(c => c.Name.Display, StringComparer.CurrentCultureIgnoreCase))
            {
                builder.AppendLine($"{component.Category,-14} {component.Name.Display}");
                builder.AppendLine($"    {_localizer["Report_Manufacturer"]}: {component.Manufacturer.Display}   {_localizer["Report_Model"]}: {component.Model.Display}");
                builder.AppendLine($"    {_localizer["Report_Status"]}: {component.Status}   {_localizer["Report_Source"]}: {component.Name.Origin.Token()}");
                if (component.Driver is { } driver)
                {
                    builder.AppendLine($"    {_localizer["Report_Driver"]}: {driver.Version.Display} ({driver.Provider.Display})  {_localizer["Report_Signature"]}: {driver.SignatureVerification}");
                }

                if (component.DeviceInstanceId.IsKnown)
                {
                    builder.AppendLine($"    {_localizer["Report_DeviceInstanceId"]}: {MaskSerial(component.DeviceInstanceId.Display, options)}");
                }
            }

            builder.AppendLine();
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Sensors")));
            builder.AppendLine(new string('-', 78));
            var sensors = request.Sensors.Count > 0 ? request.Sensors : snapshot.SensorSnapshot;
            foreach (var reading in sensors)
            {
                var value = reading.Value.HasValue
                    ? $"{reading.Value.Value!.Value.ToString(CultureInfo.CurrentCulture)} {reading.Unit}"
                    : $"UNKNOWN ({reading.Value.UnknownReason})";
                builder.AppendLine($"{_localizer[reading.NameKey],-32} {value,-24} {_localizer["Report_Quality"]}: {reading.Quality}  {_localizer["Report_MeasurementPoint"]}: {_localizer[reading.MeasurementPointKey]}");
            }

            if (sensors.Count == 0)
            {
                builder.AppendLine(_localizer["Report_NoSensors"]);
            }

            builder.AppendLine();
        }

        if (request.Updates.Count > 0)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Updates")));
            builder.AppendLine(new string('-', 78));
            foreach (var update in request.Updates)
            {
                builder.AppendLine($"{update.DeviceName.Display}: {update.Status} ({update.Installed.Raw.Display} -> {update.Available.Raw.Display})");
                builder.AppendLine($"    {_localizer["Report_Reason"]}: {_localizer.Resolve(update.Reason)}");
                builder.AppendLine($"    {_localizer["Report_Source"]}: {update.Source.AdapterId} · {update.Source.Trust} · {update.Source.Verification}");
                if (!string.IsNullOrWhiteSpace(update.BlockedReasonCode))
                {
                    builder.AppendLine($"    {_localizer["Report_Blocked"]}: {update.BlockedReasonCode}");
                }
            }

            builder.AppendLine();
        }

        if (request.Maintenance.Count > 0)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Maintenance")));
            builder.AppendLine(new string('-', 78));
            foreach (var result in request.Maintenance)
            {
                builder.AppendLine($"{result.PlanId} [{result.Mode}] {result.StartedAt:yyyy-MM-dd HH:mm:ss} — {_localizer.Resolve(result.Summary)}");
                foreach (var item in result.Items)
                {
                    builder.AppendLine($"    {item.Category,-28} {item.Outcome,-10} {item.Message is null ? string.Empty : _localizer.Resolve(item.Message)}");
                }
            }

            builder.AppendLine();
        }

        if (request.History.Count > 0)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_History")));
            builder.AppendLine(new string('-', 78));
            foreach (var entry in request.History)
            {
                builder.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}  {entry.Kind,-8} {entry.OverallStatus,-10} problems={entry.ProblemCount} changes={entry.ChangeCount}");
            }

            builder.AppendLine();
        }

        if (request.Audit.Count > 0 && options.IncludeEvidence)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Audit")));
            builder.AppendLine(new string('-', 78));
            foreach (var entry in request.Audit)
            {
                builder.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}  {entry.Operation,-14} {entry.Category,-12} {entry.Result,-10} {entry.OperationKey}");
                if (!string.IsNullOrWhiteSpace(entry.Error))
                {
                    builder.AppendLine($"    {_localizer["Report_Error"]}: {entry.Error}");
                }

                if (entry.Approval is { } approval)
                {
                    builder.AppendLine($"    {_localizer["Report_Approval"]}: {approval.Decision} ({approval.Risk})");
                }
            }

            builder.AppendLine();
        }

        if (request.Security is { } security)
        {
            builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Report_Section_Security")));
            builder.AppendLine(new string('-', 78));
            foreach (var note in security.Notes)
            {
                builder.AppendLine($"- {note}");
            }

            foreach (var hash in security.Hashes)
            {
                builder.AppendLine($"{_localizer["Report_Hash"]}: {hash.Algorithm} {hash.Hash ?? "unavailable"}  {hash.Path}");
            }

            foreach (var signature in security.Signatures)
            {
                builder.AppendLine($"{_localizer["Report_Signature"]}: {signature.Verification} {signature.Signer.Display}  {signature.Path}");
            }

            foreach (var source in security.Sources)
            {
                builder.AppendLine($"{_localizer["Report_Source"]}: {source.Url} reachable={source.Reachable} verification={source.Verification} trust={source.Trust}");
            }

            foreach (var blocked in security.BlockedOperations)
            {
                builder.AppendLine($"{_localizer["Report_Blocked"]}: {blocked.ReasonCode} — {_localizer.Resolve(blocked.Reason)}");
            }

            builder.AppendLine();
        }

        builder.AppendLine(new string('=', 78));
        builder.AppendLine(_localizer["Report_Footer_NoChanges"]);
        builder.AppendLine(_localizer["Report_Footer_Privacy"]);

        return builder.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // HTML: print ready, self contained, no external references
    // ---------------------------------------------------------------------------------------------
    private string BuildHtml(ReportRequest request, ReportOptions options)
    {
        var text = BuildText(request, options);
        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine($"<html lang=\"{_localizer.Culture.TwoLetterISOLanguageName}\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"<title>{Escape(_localizer.Resolve(LocalizedText.Of(request.IsSecurityReport ? "Report_Title_Security" : "Report_Title_System")))}</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(":root{color-scheme:light dark}body{font-family:'Segoe UI',system-ui,sans-serif;margin:2rem;line-height:1.5}");
        builder.AppendLine("pre{white-space:pre-wrap;word-wrap:break-word;font-family:'Cascadia Mono',Consolas,monospace;font-size:.92rem}");
        builder.AppendLine("header{border-bottom:2px solid #666;margin-bottom:1rem;padding-bottom:.5rem}");
        builder.AppendLine("footer{margin-top:2rem;border-top:1px solid #999;padding-top:.5rem;font-size:.85rem;color:#666}");
        builder.AppendLine("@media print{body{margin:.5cm}}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<header>");
        builder.AppendLine($"<h1>{Escape(_localizer.Resolve(LocalizedText.Of(request.IsSecurityReport ? "Report_Title_Security" : "Report_Title_System")))}</h1>");
        builder.AppendLine($"<p>{Escape(_localizer["Report_GeneratedAt"])}: {_clock.Now:yyyy-MM-dd HH:mm:ss zzz} · {Escape(_buildInfo?.Get().Version ?? "unknown")}</p>");
        if (request.Snapshot?.IsSimulation == true)
        {
            builder.AppendLine($"<p><strong>{Escape(_localizer["Report_SimulationWarning"])}</strong></p>");
        }

        builder.AppendLine("</header>");
        builder.AppendLine($"<pre>{Escape(text)}</pre>");
        builder.AppendLine("<footer>");
        builder.AppendLine(Escape(_localizer["Report_Footer_Privacy"]));
        builder.AppendLine("</footer>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    /// <summary>
    /// The measured hardware as machine readable JSON. Every leaf is either the measured value or
    /// <c>UNKNOWN: reason</c> - an empty string would hide whether a value is missing or empty
    /// (spec sections 1.3 and 44). Serial numbers, MAC and IP addresses are masked when the
    /// report options ask for it.
    /// </summary>
    private Dictionary<string, object?> BuildHardwareJson(SystemSnapshot snapshot, ReportOptions options)
    {
        var memory = snapshot.Memory;
        return new Dictionary<string, object?>
        {
            ["windows"] = new Dictionary<string, object?>
            {
                ["productName"] = Show(snapshot.Windows.ProductName),
                ["edition"] = Show(snapshot.Windows.Edition),
                ["displayVersion"] = Show(snapshot.Windows.DisplayVersion),
                ["buildNumber"] = Show(snapshot.Windows.BuildNumber),
                ["architecture"] = Show(snapshot.Windows.Architecture),
                ["isWindows11"] = YesNo(snapshot.Windows.IsWindows11),
                ["secureBootState"] = Show(snapshot.Windows.SecureBootState),
                ["activationState"] = Show(snapshot.Windows.ActivationState),
                ["uptimeHours"] = Show(snapshot.Windows.UptimeHours),
            },
            ["system"] = new Dictionary<string, object?>
            {
                ["model"] = Show(snapshot.System.ComputerModel),
                ["manufacturer"] = Show(snapshot.System.Manufacturer),
                ["systemType"] = Show(snapshot.System.SystemType),
                ["chassisType"] = Show(snapshot.System.ChassisType),
                ["serialNumber"] = MaskSerial(Show(snapshot.System.SerialNumber), options),
            },
            ["motherboard"] = new Dictionary<string, object?>
            {
                ["manufacturer"] = Show(snapshot.Motherboard.Manufacturer),
                ["product"] = Show(snapshot.Motherboard.Product),
                ["boardVersion"] = Show(snapshot.Motherboard.Version),
                ["revisionVerified"] = snapshot.Motherboard.RevisionVerified,
                ["revisionDetail"] = snapshot.Motherboard.RevisionVerificationDetail,
                ["uefiMode"] = Show(snapshot.Motherboard.UefiMode),
                ["secureBootState"] = Show(snapshot.Motherboard.SecureBootState),
            },
            ["bios"] = new Dictionary<string, object?>
            {
                ["manufacturer"] = Show(snapshot.Bios.Manufacturer),
                ["version"] = Show(snapshot.Bios.Version),
                ["releaseDate"] = Show(snapshot.Bios.ReleaseDate),
                ["smbiosVersion"] = Show(snapshot.Bios.SmbiosVersion),
                ["isUefi"] = YesNo(snapshot.Bios.IsUefi),
                ["firmwareType"] = Show(snapshot.Bios.FirmwareType),
                ["secureBootEnabled"] = YesNo(snapshot.Bios.SecureBootEnabled),
            },
            ["processors"] = snapshot.Processors.Select(p => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(p.Name),
                ["manufacturer"] = Show(p.Manufacturer),
                ["socket"] = Show(p.SocketDesignation),
                ["architecture"] = Show(p.Architecture),
                ["cores"] = Show(p.Cores),
                ["logicalProcessors"] = Show(p.LogicalProcessors),
                ["baseClockMhz"] = Show(p.BaseClockMhz),
                ["currentClockMhz"] = Show(p.CurrentClockMhz),
                ["loadPercent"] = Show(p.LoadPercent),
                ["virtualizationFirmwareEnabled"] = Show(p.VirtualizationFirmwareEnabled),
                ["origin"] = p.Name.Origin.Token(),
            }).ToList(),
            ["memory"] = new Dictionary<string, object?>
            {
                ["totalPhysicalBytes"] = Show(memory.TotalPhysicalBytes),
                ["availablePhysicalBytes"] = Show(memory.AvailablePhysicalBytes),
                ["totalSlots"] = Show(memory.TotalSlots),
                ["usedSlots"] = Show(memory.UsedSlots),
                ["usagePercent"] = Show(memory.MemoryUsagePercent),
                ["modules"] = memory.Modules.Select(m => (object)new Dictionary<string, object?>
                {
                    ["bankLabel"] = Show(m.BankLabel),
                    ["deviceLocator"] = Show(m.DeviceLocator),
                    ["capacityBytes"] = Show(m.CapacityBytes),
                    ["speedMhz"] = Show(m.SpeedMhz),
                    ["memoryType"] = Show(m.MemoryType),
                    ["formFactor"] = Show(m.FormFactor),
                    ["manufacturer"] = Show(m.Manufacturer),
                    ["partNumber"] = Show(m.PartNumber),
                    ["serialNumber"] = MaskSerial(Show(m.SerialNumber), options),
                    ["isEcc"] = YesNo(m.IsEcc),
                }).ToList(),
            },
            ["graphics"] = snapshot.Graphics.Select(g => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(g.Name),
                ["manufacturer"] = Show(g.Manufacturer),
                ["videoMemoryBytes"] = Show(g.VideoMemoryBytes),
                ["driverVersion"] = Show(g.DriverVersion),
                ["driverDate"] = Show(g.DriverDate),
                ["isIntegratedGraphics"] = g.IsIntegratedGraphics,
            }).ToList(),
            ["storage"] = snapshot.Storage.Select(d => (object)new Dictionary<string, object?>
            {
                ["friendlyName"] = Show(d.FriendlyName),
                ["model"] = Show(d.Model),
                ["serialNumber"] = MaskSerial(Show(d.SerialNumber), options),
                ["firmwareRevision"] = Show(d.FirmwareRevision),
                ["busType"] = Show(d.BusType),
                ["mediaType"] = Show(d.MediaType),
                ["sizeBytes"] = Show(d.SizeBytes),
                ["healthStatus"] = Show(d.HealthStatus),
                ["smartAvailable"] = d.SmartAvailable,
                ["isNvme"] = d.IsNvme,
                ["percentageUsed"] = Show(d.PercentageUsed),
                ["temperatureCelsius"] = Show(d.TemperatureCelsius),
                ["powerOnHours"] = Show(d.PowerOnHours),
                ["volumes"] = d.Volumes.Select(v => (object)new Dictionary<string, object?>
                {
                    ["driveLetter"] = Show(v.DriveLetter),
                    ["label"] = Show(v.Label),
                    ["fileSystem"] = Show(v.FileSystem),
                    ["sizeBytes"] = Show(v.SizeBytes),
                    ["freeBytes"] = Show(v.FreeBytes),
                    ["freePercent"] = Show(v.FreePercent),
                }).ToList(),
            }).ToList(),
            ["network"] = snapshot.Network.Select(n => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(n.Name),
                ["description"] = Show(n.Description),
                ["macAddress"] = MaskSerial(Show(n.MacAddress), options),
                ["ipAddress"] = MaskSerial(Show(n.IpAddress), options),
                ["connectionState"] = Show(n.ConnectionState),
                ["speedBitsPerSecond"] = Show(n.SpeedBitsPerSecond),
                ["driverVersion"] = Show(n.DriverVersion),
                ["isWireless"] = n.IsWireless,
                ["isBluetooth"] = n.IsBluetooth,
                ["isVirtual"] = n.IsVirtual,
            }).ToList(),
            ["monitors"] = snapshot.Monitors.Select(m => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(m.Name),
                ["manufacturer"] = Show(m.Manufacturer),
                ["productCode"] = Show(m.ProductCode),
                ["resolution"] = m.HorizontalResolution.Value.HasValue && m.VerticalResolution.Value.HasValue
                    ? $"{m.HorizontalResolution.Value.Value} x {m.VerticalResolution.Value.Value}"
                    : $"UNKNOWN: {(m.HorizontalResolution.UnknownReason ?? m.VerticalResolution.UnknownReason ?? "resolution not reported")}",
                ["refreshRate"] = Show(m.RefreshRate),
                ["manufactureYear"] = Show(m.ManufactureYear),
                ["connectionType"] = Show(m.ConnectionType),
            }).ToList(),
            ["audio"] = snapshot.Audio.Select(a => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(a.Name),
                ["manufacturer"] = Show(a.Manufacturer),
                ["status"] = Show(a.Status),
                ["driverVersion"] = Show(a.DriverVersion),
                ["isCapture"] = a.IsCapture,
            }).ToList(),
            ["printers"] = snapshot.Printers.Select(p => (object)new Dictionary<string, object?>
            {
                ["name"] = Show(p.Name),
                ["driverName"] = Show(p.DriverName),
                ["portName"] = Show(p.PortName),
                ["isDefault"] = p.IsDefault,
                ["isNetwork"] = p.IsNetwork,
            }).ToList(),
            ["battery"] = snapshot.Battery is { } battery
                ? new Dictionary<string, object?>
                {
                    ["name"] = Show(battery.Name),
                    ["manufacturer"] = Show(battery.Manufacturer),
                    ["chemistry"] = Show(battery.Chemistry),
                    ["designCapacityMwh"] = Show(battery.DesignCapacityMwh),
                    ["fullChargeCapacityMwh"] = Show(battery.FullChargeCapacityMwh),
                    ["chargePercent"] = Show(battery.ChargePercent),
                    ["cycleCount"] = Show(battery.CycleCount),
                    ["healthPercent"] = battery.HealthPercent is { } health ? health : "UNKNOWN: health not reported",
                }
                : null,
        };
    }

    /// <summary>
    /// The same inventory as readable text. Values that were not measured keep the reason, so the
    /// reader can tell "nothing to report" from "could not be read" (spec sections 1.3 and 61).
    /// </summary>
    private void AppendHardwareSection(StringBuilder builder, SystemSnapshot snapshot, ReportOptions options)
    {
        builder.AppendLine(_localizer.Resolve(LocalizedText.Of("Section_Hardware")));
        builder.AppendLine(new string('-', 78));

        builder.AppendLine($"{_localizer["Component_Cpu"]}:");
        foreach (var processor in snapshot.Processors)
        {
            builder.AppendLine($"    {Show(processor.Name)}");
            builder.AppendLine($"      {_localizer["Report_Cores"]}: {Show(processor.Cores)} / {Show(processor.LogicalProcessors)}"
                + $"   {_localizer["Report_Clock"]}: {Show(processor.CurrentClockMhz)} MHz"
                + $"   {_localizer["Report_Usage"]}: {Show(processor.LoadPercent)} %");
        }

        if (snapshot.Processors.Count == 0)
        {
            builder.AppendLine($"    {_localizer["Report_NoProcessors"]}");
        }

        var memory = snapshot.Memory;
        builder.AppendLine($"{_localizer["Component_Memory"]}:");
        builder.AppendLine($"    {_localizer["Report_Capacity"]}: {Show(memory.TotalPhysicalBytes)} B"
            + $"   {_localizer["Report_Slots"]}: {Show(memory.UsedSlots)} / {Show(memory.TotalSlots)}"
            + $"   {_localizer["Report_Usage"]}: {Show(memory.MemoryUsagePercent)} %");
        foreach (var module in memory.Modules)
        {
            builder.AppendLine($"    {Show(module.DeviceLocator)} ({Show(module.BankLabel)}): {Show(module.CapacityBytes)} B"
                + $" @ {Show(module.SpeedMhz)} MHz   {_localizer["Report_MemoryType"]}: {Show(module.MemoryType)}"
                + $"   {_localizer["Report_FormFactor"]}: {Show(module.FormFactor)}");
        }

        builder.AppendLine($"{_localizer["Component_Mainboard"]}:");
        builder.AppendLine($"    {Show(snapshot.Motherboard.Manufacturer)} {Show(snapshot.Motherboard.Product)}"
            + $"   {_localizer["Report_Revision"]}: {Show(snapshot.Motherboard.Version)}"
            + $" ({(snapshot.Motherboard.RevisionVerified ? _localizer["Report_Yes"] : _localizer["Report_No"])})");
        builder.AppendLine($"{_localizer["Component_Bios"]}:");
        builder.AppendLine($"    {Show(snapshot.Bios.Manufacturer)} {Show(snapshot.Bios.Version)} ({Show(snapshot.Bios.ReleaseDate)})"
            + $"   {_localizer["Report_SecureBootState"]}: {Show(snapshot.Motherboard.SecureBootState)}"
            + $"   {_localizer["Report_Uptime"]}: {Show(snapshot.Windows.UptimeHours)} h");

        builder.AppendLine($"{_localizer["Component_Graphics"]}:");
        foreach (var adapter in snapshot.Graphics)
        {
            builder.AppendLine($"    {Show(adapter.Name)}  {Show(adapter.VideoMemoryBytes)} B"
                + $"   {_localizer["Report_Driver"]}: {Show(adapter.DriverVersion)}");
        }

        builder.AppendLine($"{_localizer["Component_Storage"]}:");
        foreach (var device in snapshot.Storage)
        {
            builder.AppendLine($"    {Show(device.Model)} · {Show(device.BusType)} · {Show(device.SizeBytes)} B"
                + $"   {_localizer["Report_Status"]}: {Show(device.HealthStatus)}"
                + $"   {_localizer["Report_Wear"]}: {Show(device.PercentageUsed)} %"
                + $"   {_localizer["Report_Temperature"]}: {Show(device.TemperatureCelsius)} °C"
                + $"   {_localizer["Report_PowerOnHours"]}: {Show(device.PowerOnHours)} h");
            foreach (var volume in device.Volumes)
            {
                builder.AppendLine($"      {_localizer["Report_Volumes"]}: {Show(volume.DriveLetter)} ({Show(volume.FileSystem)})"
                    + $" {Show(volume.FreeBytes)} / {Show(volume.SizeBytes)} B");
            }
        }

        builder.AppendLine($"{_localizer["Component_Network"]}:");
        foreach (var adapter in snapshot.Network)
        {
            // The role is a localisation key, so the report stays in the selected language.
            var kind = adapter.IsVirtual ? "Report_Adapter_Virtual"
                : adapter.IsWireless ? "Report_Adapter_Wifi"
                : adapter.IsBluetooth ? "Report_Adapter_Bluetooth"
                : "Report_Adapter_Wired";
            builder.AppendLine($"    {Show(adapter.Description)} [{_localizer[kind]}] {Show(adapter.ConnectionState)}"
                + $"   {Show(adapter.SpeedBitsPerSecond)} bit/s   {MaskSerial(Show(adapter.MacAddress), options)}");
        }

        builder.AppendLine($"{_localizer["Component_Monitor"]}:");
        foreach (var monitor in snapshot.Monitors)
        {
            builder.AppendLine($"    {Show(monitor.Name)} {Show(monitor.HorizontalResolution)} x {Show(monitor.VerticalResolution)}"
                + $" @ {Show(monitor.RefreshRate)} Hz");
        }

        if (snapshot.Audio.Count > 0)
        {
            builder.AppendLine($"{_localizer["Component_Audio"]}:");
            foreach (var device in snapshot.Audio)
            {
                builder.AppendLine($"    {Show(device.Name)}  {_localizer["Report_Status"]}: {Show(device.Status)}");
            }
        }

        if (snapshot.Printers.Count > 0)
        {
            builder.AppendLine($"{_localizer["Component_Printer"]}:");
            foreach (var printer in snapshot.Printers)
            {
                builder.AppendLine($"    {Show(printer.Name)}  {_localizer["Report_Driver"]}: {Show(printer.DriverName)}");
            }
        }

        if (snapshot.Battery is { } battery)
        {
            builder.AppendLine($"{_localizer["Component_Battery"]}:");
            builder.AppendLine($"    {Show(battery.Name)}  {Show(battery.DesignCapacityMwh)} / {Show(battery.FullChargeCapacityMwh)} mWh"
                + $"   {_localizer["Report_Usage"]}: {Show(battery.ChargePercent)} %");
        }

        builder.AppendLine();
    }

    /// <summary>Text of a measured fact, or UNKNOWN with the reason that was recorded.</summary>
    private static string Show(TextInfo info) =>
        info.IsKnown ? info.Value! : $"UNKNOWN: {info.UnknownReason ?? "reason not reported"}";

    /// <summary>Text of a measured number, or UNKNOWN with the reason that was recorded.</summary>
    private static string Show<T>(Measured<T> value, string? format = null) =>
        value.Value.HasValue
            ? value.Display(CultureInfo.InvariantCulture, format)
            : $"UNKNOWN: {value.UnknownReason ?? "reason not reported"}";

    private static string MaskSerial(string value, ReportOptions options) =>
        options.MaskSerialNumbers && value.Length > 4 ? $"…{value[^4..]}" : value;

    /// <summary>Tri-state flag: a flag that was not reported stays visible as unknown.</summary>
    private static string YesNo(bool? value) =>
        value.HasValue ? (value.Value ? "true" : "false") : "UNKNOWN: not reported";

    private static string Sanitise(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "system-report" : cleaned;
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
