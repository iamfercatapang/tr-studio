using System.Security.Principal;

namespace CoreVision.App;

static class Program
{
    const string UninstallCleanup = "--uninstall-cleanup";

    /// <summary>WebView2 and the file dialogs need an STA thread, which top-level statements don't provide.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        var log = new FileLog();
        using var identity = WindowsIdentity.GetCurrent();
        log.Write($"Launch: admin={new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}, args=[{string.Join(' ', args)}]");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Crash: {e.ExceptionObject}");
        Application.ThreadException += (_, e) => log.Write($"UI error: {e.Exception}");

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

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(startHidden: args.Contains("--background")));
        }
        catch (Exception e)
        {
            log.Write($"Startup failed: {e}");
            throw;
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
