using System.Runtime.InteropServices;
using System.Security.Principal;
using WindowsMaintenanceCenter.Core;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// Determines whether the current process runs with administrator rights (spec section 52).
/// The check inspects the process token instead of environment variables and degrades to
/// <see cref="SessionPrivilege.Unknown"/> when the token cannot be inspected - it never
/// guesses "elevated".
/// </summary>
public static class ElevationProbe
{
    private const uint TokenQuery = 0x0008;
    private const uint TokenElevationClass = 20;

    public static SessionPrivilege Current()
    {
        if (!OperatingSystem.IsWindows())
        {
            return SessionPrivilege.Unknown;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.IsSystem)
            {
                return SessionPrivilege.Administrator;
            }

            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                return SessionPrivilege.StandardUser;
            }

            // The account is an administrator; only an elevated token may change the system.
            return HasElevatedToken() ? SessionPrivilege.Administrator : SessionPrivilege.StandardUser;
        }
        catch (Exception)
        {
            return SessionPrivilege.Unknown;
        }
    }

    private static bool HasElevatedToken()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token) || token == IntPtr.Zero)
            {
                return false;
            }

            if (!GetTokenInformation(token, (int)TokenElevationClass, out var elevation, sizeof(int), out _))
            {
                return false;
            }

            return elevation != 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
