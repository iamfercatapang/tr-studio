using System.Collections.Concurrent;
using CoreVision.Engine;
using Sentry;

namespace CoreVision.App;

/// <summary>
/// Sends crash reports to Sentry, only after the user says yes. Off, it never starts, so nothing leaves the PC.
/// A report holds the error, its stack trace, the app and Windows versions, the screen model and the end of the log,
/// with the user name, the PC's name and user folders taken out.
/// </summary>
static class CrashReporter
{
    /// <summary>
    /// The Sentry project's DSN. It can only send events, so it is safe to ship. Empty turns reporting off entirely.
    /// </summary>
    public const string Dsn = "https://2ee6c402540e589c8d2fff09e5a0481f@o4512371470761984.ingest.us.sentry.io/4512371491471360";

    const int LogLines = 50;
    static readonly object Gate = new();
    static readonly ConcurrentDictionary<string, byte> Reported = new();
    static IDisposable? _sentry;
    static FileLog? _log;

    /// <summary>The cooler model, added to reports once the screen is found.</summary>
    public static string? ScreenModel { get; set; }

    public static bool IsAvailable => Dsn.Length > 0;

    public static bool IsRunning
    {
        get { lock (Gate) return _sentry != null; }
    }

    /// <summary>Starts or stops reporting to match the user's choice. Safe to call any time.</summary>
    public static void Apply(bool? enabled, FileLog log)
    {
        lock (Gate)
        {
            _log = log;
            if (enabled == true && IsAvailable && _sentry == null) Start(log);
            else if (enabled != true && _sentry != null)
            {
                _sentry.Dispose();
                _sentry = null;
                log.Write("Crash reports off");
            }
        }
    }

    static void Start(FileLog log)
    {
        try
        {
            _sentry = SentrySdk.Init(options =>
            {
                options.Dsn = Dsn;
                options.Release = $"tr-studio@{UpdateService.Current}";
#if DEBUG
                options.Environment = "debug";
#else
                options.Environment = "release";
#endif
                options.IsGlobalModeEnabled = true;
#if DEBUG
                options.Debug = Environment.GetEnvironmentVariable("TRSTUDIO_SENTRY_DEBUG") == "1";
#endif
                options.SendDefaultPii = false;
                options.AutoSessionTracking = false;
                options.ServerName = "";
                options.MaxBreadcrumbs = LogLines;
                options.SetBeforeSend((report, _) => Clean(report));
                options.SetBeforeBreadcrumb((crumb, _) => null); // only the log lines added in Clean
            });
            log.Write("Crash reports on");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            _sentry = null;
            log.Write($"Crash reports could not start: {e.Message}");
        }
    }

    /// <summary>A bug that was caught and logged. Each distinct message is sent once a session, so a failing loop can't flood reports.</summary>
    public static void ReportOnce(Exception error, string where)
    {
        if (!IsRunning || !Reported.TryAdd($"{where}|{error.GetType().FullName}|{error.Message}", 0)) return;
        SentrySdk.CaptureException(error, scope =>
        {
            scope.Level = SentryLevel.Error;
            scope.SetTag("where", where);
        });
    }

    static string ScrubText(string? text) => CrashLog.Scrub(text ?? "", Environment.UserName, Environment.MachineName,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    static SentryEvent? Clean(SentryEvent report)
    {
        report.ServerName = null;
        report.User = new SentryUser();
        report.Contexts.Device.Name = null;
        if (report.Message is { } message)
        {
            message.Message = ScrubText(message.Message);
            message.Formatted = ScrubText(message.Formatted);
        }
        foreach (var exception in report.SentryExceptions ?? [])
        {
            exception.Value = ScrubText(exception.Value);
            foreach (var frame in exception.Stacktrace?.Frames ?? [])
            {
                frame.AbsolutePath = frame.AbsolutePath is { } path ? ScrubText(path) : null;
                frame.FileName = frame.FileName is { } file ? ScrubText(file) : null;
            }
        }
        report.SetTag("screen", ScreenModel ?? "none");
        report.SetTag("windows", Environment.OSVersion.Version.ToString());
        report.SetExtra("log", string.Join('\n', (_log?.Tail(LogLines) ?? []).Select(ScrubText)));
        return report;
    }
}
