using System.Security.Principal;

namespace CoreVision.App;

static class Program
{
    /// <summary>WebView2 and the file dialogs need an STA thread, which top-level statements don't provide.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        var log = new FileLog();
        using var identity = WindowsIdentity.GetCurrent();
        log.Write($"Launch: admin={new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}, args=[{string.Join(' ', args)}]");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Crash: {e.ExceptionObject}");
        Application.ThreadException += (_, e) => log.Write($"UI error: {e.Exception}");

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
}
