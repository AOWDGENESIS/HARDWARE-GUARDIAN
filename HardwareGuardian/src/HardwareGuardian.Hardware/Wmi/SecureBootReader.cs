using System.Runtime.InteropServices;

namespace HardwareGuardian.Hardware.Wmi;

/// <summary>
/// The result of reading the UEFI <c>SecureBoot</c> variable.
/// <see cref="Value"/> is <c>null</c> whenever the state could not be measured;
/// <see cref="Detail"/> then says why, and it never claims a state (spec sections 1.3 and 91).
/// </summary>
public readonly record struct SecureBootReading(bool? Value, string Detail)
{
    /// <summary>True when a state was really measured.</summary>
    public bool IsKnown => Value.HasValue;

    /// <summary>
    /// Text for the UI: "Enabled"/"Disabled" when measured, otherwise the reason it is unknown.
    /// </summary>
    public string Display => Value switch
    {
        true => "Enabled",
        false => "Disabled",
        null => Detail,
    };
}

/// <summary>
/// Reads the firmware variable that carries the Secure Boot state and interprets the raw result
/// of <c>GetFirmwareEnvironmentVariableW</c>.
///
/// Why this is a separate, pure function: the earlier implementation treated
/// <c>ERROR_INSUFFICIENT_BUFFER</c> (122) as "Secure Boot is on". That is a guess - 122 only says
/// that the caller's buffer did not fit, and on a machine without an EFI variable store the call
/// fails without any state being readable. A security statement that is guessed is worse than no
/// statement, so the interpretation is separated from the P/Invoke and covered by tests.
///
/// Documented facts used here (UEFI specification, section 3.3 "Secure Boot"): the variable is a
/// UINT8, 0 means disabled and 1 means enabled.
/// </summary>
public static class SecureBootReader
{
    private const string SecureBootVariable = "SecureBoot";
    private const string GlobalVariableGuid = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";
    private const int DocumentedSize = 1;
    private const int FallbackSize = 8;

    /// <summary>ERROR_INSUFFICIENT_BUFFER - the variable exists but is larger than the buffer.</summary>
    public const int ErrorInsufficientBuffer = 122;

    /// <summary>ERROR_INVALID_FUNCTION - the firmware has no EFI variable store (legacy BIOS/VBR).</summary>
    public const int ErrorInvalidFunction = 1;

    /// <summary>ERROR_FILE_NOT_FOUND - the variable does not exist.</summary>
    public const int ErrorFileNotFound = 2;

    /// <summary>ERROR_ACCESS_DENIED - reading firmware variables needs administrator rights.</summary>
    public const int ErrorAccessDenied = 5;

    /// <summary>ERROR_NOT_SUPPORTED - the running system does not provide firmware variables.</summary>
    public const int ErrorNotSupported = 50;

    /// <summary>
    /// Interprets the raw outcome of the call. <paramref name="bytesReturned"/> is 0 when the call
    /// failed; <paramref name="lastError"/> is only meaningful in that case.
    /// </summary>
    public static SecureBootReading Interpret(uint bytesReturned, int lastError, ReadOnlySpan<byte> buffer)
    {
        if (bytesReturned == 0)
        {
            return new SecureBootReading(null, ExplainError(lastError));
        }

        if (buffer.Length == 0)
        {
            // The call reported bytes but handed back nothing: contradictory, so no state.
            return new SecureBootReading(null, $"{bytesReturned} byte(s) reported but no data was written");
        }

        var value = buffer[0] != 0;
        var detail = bytesReturned == DocumentedSize
            ? $"SecureBoot variable ({DocumentedSize} byte, UEFI UINT8)"
            : $"SecureBoot variable reported {bytesReturned} byte(s) instead of the documented {DocumentedSize}; the first byte was used";
        return new SecureBootReading(value, detail);
    }

    /// <summary>
    /// Sends the reason an unreadable variable is not a state. Kept public and pure for the tests.
    /// </summary>
    public static string ExplainError(int lastError) => lastError switch
    {
        ErrorInsufficientBuffer =>
            "Secure Boot state is unknown: the firmware reported ERROR_INSUFFICIENT_BUFFER (122), so the variable size does not match the documented UINT8 - this says nothing about the state",
        ErrorInvalidFunction =>
            "Secure Boot state is unknown: the firmware has no EFI variable store (legacy BIOS or virtual machine without UEFI)",
        ErrorFileNotFound =>
            "Secure Boot state is unknown: the SecureBoot variable does not exist on this firmware",
        ErrorAccessDenied =>
            "Secure Boot state is unknown: reading firmware variables was denied (administrator rights are required)",
        ErrorNotSupported =>
            "Secure Boot state is unknown: this system does not provide firmware variables",
        0 => "Secure Boot state is unknown: the call returned no data and no error",
        _ => $"Secure Boot state is unknown: GetFirmwareEnvironmentVariable failed with the Windows error {lastError}",
    };

    /// <summary>
    /// Reads the variable. Returns an unknown reading on every non-Windows system, on any Windows
    /// error and on any exception - never a guessed state.
    /// </summary>
    public static SecureBootReading Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SecureBootReading(null, "Secure Boot state is unknown: the firmware API only exists on Windows");
        }

        try
        {
            var buffer = new byte[DocumentedSize];
            var result = NativeMethods.GetFirmwareEnvironmentVariable(
                SecureBootVariable,
                GlobalVariableGuid,
                buffer,
                (uint)buffer.Length);

            if (result > 0)
            {
                return Interpret(result, 0, buffer);
            }

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorInsufficientBuffer)
            {
                return Interpret(0, error, buffer);
            }

            // The variable exists but did not fit into the documented single byte. Read it once
            // with a larger buffer and keep the deviation inside the detail text instead of
            // declaring a state that this buffer size cannot prove.
            var wider = new byte[FallbackSize];
            var retry = NativeMethods.GetFirmwareEnvironmentVariable(
                SecureBootVariable,
                GlobalVariableGuid,
                wider,
                (uint)wider.Length);

            return retry > 0
                ? Interpret(retry, 0, wider)
                : Interpret(0, Marshal.GetLastWin32Error(), wider);
        }
        catch (Exception exception)
        {
            return new SecureBootReading(null, $"Secure Boot state is unknown: {exception.GetType().Name} while reading the firmware variable");
        }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFirmwareEnvironmentVariable(string name, string guid, byte[] buffer, uint size);
}
