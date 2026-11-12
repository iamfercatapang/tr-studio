using System.Diagnostics;
using TRStudio.Engine;

namespace TRStudio.App;

/// <summary>Runs the app at sign-in through a scheduled task. TRCC stays installed; only its own startup task is paused.</summary>
static class WindowsStartup
{
    const int TimeoutMs = 10_000;

    public static bool IsEnabled() => Run(StartupCommands.Query()).ExitCode == 0;

    /// <returns>True if TRCC's startup task was turned off by this call.</returns>
    public static bool Enable()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The app's location is unknown.");
        Require(Run(StartupCommands.Create(path)));
        if (Run(StartupCommands.Query(StartupCommands.TrccTaskName)).ExitCode != 0) return false;
        Require(Run(StartupCommands.SetTrccEnabled(false)));
        return true;
    }

    /// <summary>An installed or moved copy takes the task over, so sign-in never starts an old one.</summary>
    public static void PointAtThisCopy()
    {
        if (IsEnabled() && Environment.ProcessPath is { } path) Require(Run(StartupCommands.Create(path)));
    }

    public static void Disable(bool restoreTrcc)
    {
        Require(Run(StartupCommands.Delete()));
        // TRCC may have been uninstalled since its task was paused.
        if (restoreTrcc && Run(StartupCommands.Query(StartupCommands.TrccTaskName)).ExitCode == 0) Require(Run(StartupCommands.SetTrccEnabled(true)));
    }

    static void Require((int ExitCode, string Output) result)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Output.Trim());
    }

    static (int ExitCode, string Output) Run(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // schtasks must never wait for a typed answer
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("schtasks did not start.");
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeoutMs))
        {
            process.Kill();
            return (-1, "Windows took too long to respond.");
        }
        return (process.ExitCode, output + error.Result);
    }
}
