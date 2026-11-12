using System.Security.Principal;

namespace TRStudio.App;

static class Program
{
    const string UninstallCleanup = "--uninstall-cleanup";

    /// <summary>WebView2 and the file dialogs need an STA thread, which top-level statements don't provide.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        var log = new FileLog();
        var crashedLastTime = log.LastSessionCrashed();
        using var identity = WindowsIdentity.GetCurrent();
        log.Write($"Launch: admin={new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}, args=[{string.Join(' ', args)}]");
        var testCrash = false;
#if DEBUG
        testCrash = args.Contains("--test-crash"); // reports this one crash whatever the setting, to check Sentry end to end
#endif
        CrashReporter.Apply(testCrash ? true : Settings.Load().CrashReports, log);
        // Sentry catches crashes and lost background errors itself, and sends them before the app closes.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Crash: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => log.Write($"Background error: {e.Exception}");
        Application.ThreadException += (_, e) =>
        {
            log.Write($"UI error: {e.Exception}");
            CrashReporter.ReportOnce(e.Exception, "ui"); // WinForms keeps running after these, so Sentry never sees them
        };
        if (testCrash) throw new InvalidOperationException($"Test crash from {Environment.UserName} on {Environment.MachineName} in {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");

        if (args.Contains(UninstallCleanup))
        {
            CleanUpForUninstall(log);
            return;
        }

        try
        {
            using var instance = new Mutex(true, "CoreVisionStudio.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                log.Write("Already running; exiting.");
                return;
            }

            if (PowerThrottling.OptOut() is { } refused) log.Write($"Power throttling opt-out: {refused}");
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(startHidden: args.Contains("--background"), log, crashedLastTime));
        }
        catch (Exception e)
        {
            log.Write($"Startup failed: {e}");
            throw; // reported as a crash by the handler above
        }
    }

    /// <summary>A failure here is logged and not raised, because it must not stop the uninstall.</summary>
    static void CleanUpForUninstall(FileLog log)
    {
        try
        {
            if (WindowsStartup.IsEnabled()) WindowsStartup.Disable(Settings.Load().PausedTrccStartup);
            log.Write("Removed the Windows startup task for uninstall");
        }
        catch (InvalidOperationException e) { log.Write($"Uninstall cleanup failed: {e.Message}"); }
    }
}
