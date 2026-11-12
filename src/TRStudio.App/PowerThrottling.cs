using System.Runtime.InteropServices;

namespace TRStudio.App;

/// <summary>
/// Windows 11 slows down a process that has no visible window, which is TR Studio in the tray: it ignores the
/// 1 ms timer the lighting asks for (memory lighting then takes about 31 ms a transfer instead of 1.2 ms, so
/// effects stutter after the window is hidden or the PC wakes) and runs it on the slow EcoQoS cores. The
/// screen and the lights are live output, so the app opts out of both.
/// </summary>
static class PowerThrottling
{
    const int ProcessPowerThrottling = 4;
    const uint CurrentVersion = 1;
    const uint ExecutionSpeed = 0x1;
    const uint IgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct State
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    /// <summary>Returns null when it worked, else why not. Windows before 11 doesn't know the timer flag and refuses; nothing changes then.</summary>
    public static string? OptOut()
    {
        var state = new State { Version = CurrentVersion, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
        if (SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<State>())) return null;
        // Windows 10 refuses the timer flag; keep at least full speed there.
        state.ControlMask = ExecutionSpeed;
        var error = Marshal.GetLastWin32Error();
        return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<State>())
            ? $"timer opt-out refused (error {error}), full speed kept"
            : $"refused (error {Marshal.GetLastWin32Error()})";
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref State info, uint size);

    [DllImport("kernel32")]
    static extern IntPtr GetCurrentProcess();
}
