using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Events;

/// <summary>
/// Internal event bus payloads (spec section 69). The UI subscribes to these; nothing else
/// needs to poll the services.
/// </summary>
public sealed record ScanStartedEvent(string ScanId, int ModuleCount, DateTimeOffset StartedAt);

public sealed record ScanCompletedEvent(string ScanId, SystemSnapshot Snapshot);

public sealed record ModuleCompletedEvent(ModuleResult Result);

public sealed record HardwareDetectedEvent(ComponentCategory Category, string ComponentId, string DisplayName);

public sealed record DriverCheckedEvent(string ComponentId, bool? IsSigned, VerificationLevel Verification, HealthStatus Status);

public sealed record ManufacturerQueryStartedEvent(string AdapterId, string DisplayNameKey);

public sealed record ManufacturerQueryCompletedEvent(SourceCheckResult Result);

public sealed record WarningDetectedEvent(Problem Problem);

public sealed record ErrorDetectedEvent(Problem Problem);

public sealed record CriticalErrorDetectedEvent(Problem Problem);

public sealed record ProblemDetectedEvent(Problem Problem);

public sealed record ProblemStatusChangedEvent(string ProblemId, ProblemStatus OldStatus, ProblemStatus NewStatus);

public sealed record ActionBlockedEvent(BlockedOperation Blocked);

public sealed record ApprovalRequestedEvent(ApprovalRequest Request);

public sealed record ApprovalDecidedEvent(ApprovalRequest Request);

public sealed record ActionStartedEvent(OperationKind Operation, string OperationId, string OperationKey, RiskLevel Risk);

public sealed record ActionCompletedEvent(OperationKind Operation, string OperationId, StageOutcome Outcome, string? Error);

public sealed record BackupCreatedEvent(BackupRecord Record);

public sealed record RollbackStartedEvent(string BackupRecordId);

public sealed record RollbackCompletedEvent(RollbackResult Result);

public sealed record StateChangedEvent(SystemState Previous, SystemState Current, DateTimeOffset ChangedAt, string? Reason);

public sealed record ProgressChangedEvent(ProgressSnapshot Snapshot);

public sealed record UpdateAssessmentCompletedEvent(UpdateAssessment Assessment);

public sealed record MaintenanceCompletedEvent(MaintenanceResult Result);

public sealed record SensorSampledEvent(SensorSnapshot Snapshot);

public sealed record LanguageChangedEvent(string CultureName);

public sealed record ThemeChangedEvent(ThemePreference Theme);

public sealed record NotificationRaisedEvent(NotificationMessage Message);

public sealed record ProtocolEntryEvent(ProtocolEntry Entry);
