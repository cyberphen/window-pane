using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WindowPane;

/// <summary>
/// Fail-closed checks before cross-process SetParent (UIPI / session / user).
/// </summary>
internal static class TargetPolicy
{
    public static bool IsAllowed(IntPtr hwnd, out string? error)
    {
        error = null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            error = "Could not identify the window process.";
            return false;
        }

        if ((int)pid == Environment.ProcessId)
        {
            error = "Cannot capture Window Pane's own windows.";
            return false;
        }

        try
        {
            using var target = Process.GetProcessById((int)pid);
            // Touch MainModule only for existence; may throw for protected processes.
            _ = target.Id;
        }
        catch
        {
            error = "Cannot open that process (protected or exited).";
            return false;
        }

        if (!TryGetTokenSnapshot((int)pid, out var targetSnap, out var targetErr))
        {
            error = targetErr ?? "Cannot read target security token.";
            return false;
        }

        if (!TryGetTokenSnapshot(Environment.ProcessId, out var selfSnap, out var selfErr))
        {
            error = selfErr ?? "Cannot read own security token.";
            return false;
        }

        if (!string.Equals(selfSnap.UserSid, targetSnap.UserSid, StringComparison.OrdinalIgnoreCase))
        {
            error = "Window belongs to a different user.";
            return false;
        }

        if (selfSnap.SessionId != targetSnap.SessionId)
        {
            error = "Window is in a different session.";
            return false;
        }

        // Equal integrity only — never dock higher-IL (elevated) into medium IL, or vice versa.
        if (selfSnap.IntegrityRid != targetSnap.IntegrityRid)
        {
            error = selfSnap.IntegrityRid < targetSnap.IntegrityRid
                ? "That window is elevated (higher integrity). Run Window Pane elevated only if you intentionally need that, or dock non-elevated apps."
                : "Integrity level mismatch — capture blocked for safety.";
            return false;
        }

        if (targetSnap.IsElevated && !selfSnap.IsElevated)
        {
            error = "Cannot dock an elevated window into a non-elevated pane.";
            return false;
        }

        return true;
    }

    private readonly record struct TokenSnapshot(string UserSid, int SessionId, int IntegrityRid, bool IsElevated);

    private static bool TryGetTokenSnapshot(int pid, out TokenSnapshot snap, out string? error)
    {
        snap = default;
        error = null;
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            error = "PROCESS_QUERY_LIMITED_INFORMATION denied.";
            return false;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token) || token == IntPtr.Zero)
            {
                error = "OpenProcessToken failed.";
                return false;
            }

            try
            {
                using var identity = new WindowsIdentity(token);
                var sid = identity.User?.Value ?? "";
                if (string.IsNullOrEmpty(sid))
                {
                    error = "Missing user SID.";
                    return false;
                }

                if (!NativeMethods.ProcessIdToSessionId((uint)pid, out var session))
                {
                    error = "ProcessIdToSessionId failed.";
                    return false;
                }

                var integrity = ReadIntegrityRid(token);
                var elevated = ReadElevation(token);
                snap = new TokenSnapshot(sid, (int)session, integrity, elevated);
                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static int ReadIntegrityRid(IntPtr token)
    {
        // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel = 25
        const int TokenIntegrityLevel = 25;
        NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var needed);
        if (needed == 0) return -1;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out _))
                return -1;

            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label }
            var sidAndAttrs = Marshal.PtrToStructure<NativeMethods.SID_AND_ATTRIBUTES>(buffer);
            var subAuthCount = Marshal.ReadByte(sidAndAttrs.Sid, 1);
            // Last sub-authority is the integrity RID (e.g. 0x2000 medium, 0x3000 high)
            var ridOffset = 8 + (subAuthCount - 1) * 4;
            return Marshal.ReadInt32(sidAndAttrs.Sid, ridOffset);
        }
        catch
        {
            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadElevation(IntPtr token)
    {
        // TokenElevation = 20
        const int TokenElevation = 20;
        var size = Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.GetTokenInformation(token, TokenElevation, buffer, size, out _))
                return false;
            var elev = Marshal.PtrToStructure<NativeMethods.TOKEN_ELEVATION>(buffer);
            return elev.TokenIsElevated != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
