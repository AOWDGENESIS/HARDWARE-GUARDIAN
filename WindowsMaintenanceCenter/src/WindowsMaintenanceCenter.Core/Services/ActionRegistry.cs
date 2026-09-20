using System.Globalization;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// The action registry (WMC specification, chapter 38). Every action the application may perform is
/// declared here; nothing else can be performed. Two rules are the point of this class:
///
/// * An action that is not registered is refused before any program is started (M32-SEC-001).
/// * Every argument is checked against its declared shape, and a path argument may not leave its
///   permitted area (M32-SEC-002/003). Chapter 78 and 79 name the exact inputs that have to be
///   handled safely: <c>" ' ; &amp; | &gt; &lt; $ ( ) { } .. ..\ %PATH%</c> - none of them may lead to a
///   second action.
///
/// The registry itself executes nothing. It answers one question: "may this be run, and with which
/// arguments?" The execution lives in <c>IAdminWorker</c> (chapter 37), which asks the registry
/// first and refuses when the answer is no.
/// </summary>
public interface IActionRegistry
{
    IReadOnlyCollection<RegisteredAction> All { get; }

    bool TryGet(string actionId, out RegisteredAction action);

    /// <summary>
    /// Checks a request completely: registered, allowed, arguments present, arguments valid, path
    /// policy. The resolved command line is returned so that the caller never builds a command
    /// itself.
    /// </summary>
    ActionValidationResult Validate(ActionRequest request);
}

/// <summary>Default registry. Registration happens once at start up, before any interface exists.</summary>
public sealed class ActionRegistry : IActionRegistry
{
    private readonly Dictionary<string, RegisteredAction> _actions = new(StringComparer.Ordinal);

    public ActionRegistry(IEnumerable<RegisteredAction>? actions = null)
    {
        if (actions is null)
        {
            return;
        }

        foreach (var action in actions)
        {
            Register(action);
        }
    }

    public void Register(RegisteredAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Id))
        {
            throw new ArgumentException("An action needs an identifier.", nameof(action));
        }

        // An action that overwrites another one would be invisible in the registry; refuse instead.
        if (!_actions.TryAdd(action.Id, action))
        {
            throw new InvalidOperationException($"The action '{action.Id}' is registered twice.");
        }
    }

    public IReadOnlyCollection<RegisteredAction> All => _actions.Values.ToList();

    public bool TryGet(string actionId, out RegisteredAction action)
    {
        action = null!;
        return !string.IsNullOrWhiteSpace(actionId) && _actions.TryGetValue(actionId, out action!);
    }

    public ActionValidationResult Validate(ActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGet(request.ActionId, out var action))
        {
            return ActionValidationResult.Invalid(BlockReasons.ActionNotRegistered, new ActionValidationError { Key = "Action_Blocked_NotRegistered" });
        }

        if (!action.Allowed)
        {
            return ActionValidationResult.Invalid(BlockReasons.ActionNotAllowed, new ActionValidationError { Key = "Action_Blocked_NotAllowed" });
        }

        if (string.IsNullOrWhiteSpace(action.Executable))
        {
            return ActionValidationResult.Invalid(BlockReasons.InvalidRequest, new ActionValidationError { Key = "Action_Blocked_NoExecutable" });
        }

        // Chapter 44 orders the gates: BACKUP before APPROVAL before EXECUTE. Both are checked here,
        // before a single argument is looked at, and both are bound to this action: a record for
        // another action is not a permission for this one.
        if (action.RequiresBackup && !BackupCoversAction(action, request.Backup))
        {
            return ActionValidationResult.Invalid(
                BlockReasons.BackupRequired,
                new ActionValidationError { Key = "Action_Blocked_BackupRequired" });
        }

        if (action.RequiresApproval)
        {
            if (request.Approval is null || request.Approval.Decision != ApprovalDecision.Approved)
            {
                return ActionValidationResult.Invalid(
                    BlockReasons.ApprovalMissing,
                    new ActionValidationError { Key = "Action_Blocked_ApprovalMissing" });
            }

            if (!string.Equals(request.Approval.OperationId, action.Id, StringComparison.Ordinal))
            {
                // An approval the user gave for something else never authorises this action.
                return ActionValidationResult.Invalid(
                    BlockReasons.ApprovalMissing,
                    new ActionValidationError { Key = "Action_Blocked_ApprovalMismatch" });
            }
        }

        var errors = new List<ActionValidationError>();
        var resolved = new List<string>();

        foreach (var key in request.Arguments.Keys)
        {
            if (!action.Arguments.Any(spec => string.Equals(spec.Name, key, StringComparison.Ordinal)))
            {
                errors.Add(new ActionValidationError { Key = "Action_Unknown_Argument", Argument = key });
            }
        }

        foreach (var spec in action.Arguments)
        {
            var present = request.Arguments.TryGetValue(spec.Name, out var value);
            if (!present || string.IsNullOrEmpty(value))
            {
                if (spec.Required)
                {
                    errors.Add(new ActionValidationError { Key = "Action_Missing_Argument", Argument = spec.Name });
                }

                continue;
            }

            var verdict = CheckArgument(spec, value!);
            if (verdict is not null)
            {
                errors.Add(verdict);
            }
        }

        if (errors.Count > 0)
        {
            // A refused path is a different reason than a refused value: the first is the path policy
            // of chapter 79, the second is an argument that does not match its declaration.
            var reason = errors.Any(error => error.Key is "Action_Path_Traversal" or "Action_Path_UncOrDevice")
                ? BlockReasons.ActionPathNotAllowed
                : BlockReasons.ActionArgumentInvalid;

            return ActionValidationResult.Invalid(reason, errors.ToArray());
        }

        foreach (var part in action.ArgumentTemplate)
        {
            resolved.Add(Substitute(part, action, request.Arguments));
        }

        return new ActionValidationResult
        {
            IsValid = true,
            Action = action,
            ResolvedArguments = resolved,
        };
    }

    /// <summary>
    /// A backup record only counts for this action when it names the action and carries an artifact
    /// or a manifest - an empty record is a claim, not a secured state.
    /// </summary>
    private static bool BackupCoversAction(RegisteredAction action, BackupRecord? backup) =>
        backup is not null
        && string.Equals(backup.OperationId, action.Id, StringComparison.Ordinal)
        && (backup.ArtifactPath is not null || backup.ManifestPath is not null);

    /// <summary>
    /// Validates one value. Returns null when the value is acceptable, otherwise the reason as text.
    /// The reasons are localisation keys with the argument name filled in, so the interface can show
    /// exactly which value was refused and why.
    /// </summary>
    private static ActionValidationError? CheckArgument(ActionArgumentSpec spec, string value)
    {
        if (value.Length > spec.MaxLength)
        {
            return Refuse("Action_Argument_TooLong", spec.Name);
        }

        if (value.Any(char.IsControl))
        {
            return Refuse("Action_Text_Metacharacter", spec.Name);
        }

        switch (spec.Kind)
        {
            case ActionArgumentKind.Number:
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? null
                    : Refuse("Action_Argument_NotANumber", spec.Name);

            case ActionArgumentKind.Choice:
                return spec.AllowedValues.Contains(value, StringComparer.Ordinal)
                    ? null
                    : Refuse("Action_Argument_NotAllowedValue", spec.Name);

            case ActionArgumentKind.Switch:
                return bool.TryParse(value, out _) ? null : Refuse("Action_Argument_NotAllowedValue", spec.Name);

            case ActionArgumentKind.Path:
                return CheckPath(spec, value);

            default:
                return ContainsShellMetacharacter(value) ? Refuse("Action_Text_Metacharacter", spec.Name) : null;
        }
    }

    private static ActionValidationError Refuse(string key, string argument) => new() { Key = key, Argument = argument };

    /// <summary>
    /// Path policy of chapter 79: no parent directory, no UNC and no device path, no shell
    /// metacharacter. A value that fails here never reaches a command line.
    /// </summary>
    private static ActionValidationError? CheckPath(ActionArgumentSpec spec, string value)
    {
        if (ContainsShellMetacharacter(value))
        {
            return Refuse("Action_Path_Metacharacter", spec.Name);
        }

        var normalized = value.Replace('/', '\\');

        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC (\\server\share) and device paths (\\?\, \\.\) - neither is needed by an action
            // that is registered today.
            return Refuse("Action_Path_UncOrDevice", spec.Name);
        }

        if (normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            return Refuse("Action_Path_Traversal", spec.Name);
        }

        if (!spec.AllowSpaces && value.Contains(' ', StringComparison.Ordinal))
        {
            return Refuse("Action_Path_Metacharacter", spec.Name);
        }

        return null;
    }

    /// <summary>
    /// The characters of chapter 78, prepared once: they are refused in every argument that is not a
    /// number or a choice, whichever position they appear in.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> ShellMetacharacters =
        System.Buffers.SearchValues.Create("\"';&|><$`(){}%^");

    /// <summary>
    /// The characters of chapter 78. They are refused in every argument that is not a number or a
    /// choice, whichever position they appear in.
    /// </summary>
    private static bool ContainsShellMetacharacter(string value) =>
        value.AsSpan().IndexOfAny(ShellMetacharacters) >= 0;

    /// <summary>
    /// Replaces <c>{Name}</c> in a template part with the validated value. A placeholder without a
    /// value disappears together with its whole part, so that no empty argument is handed to a tool.
    /// </summary>
    private static string Substitute(string part, RegisteredAction action, IReadOnlyDictionary<string, string> values)
    {
        var result = part;
        foreach (var spec in action.Arguments)
        {
            var token = "{" + spec.Name + "}";
            if (!result.Contains(token, StringComparison.Ordinal))
            {
                continue;
            }

            result = result.Replace(token, values.TryGetValue(spec.Name, out var value) ? value : string.Empty, StringComparison.Ordinal);
        }

        return result;
    }

}

/// <summary>Helpers for the actions the application registers at start up.</summary>
public static class RegisteredActionFactory
{
    /// <summary>Identifier form of chapter 32: <c>Area.WhatItDoes</c>.</summary>
    public static string Identifier(string area, string what) => $"{area}.{what}";
}
