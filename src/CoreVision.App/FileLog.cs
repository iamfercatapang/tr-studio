namespace CoreVision.App;

sealed class FileLog
{
    const long MaxBytes = 1024 * 1024;
    readonly string _path;
    readonly object _gate = new();

    public FileLog()
    {
        // Not the per-user folder: an elevated app may not resolve to the signed-in user's profile.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CoreVisionStudio", "logs");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "engine.log");
    }

    public string Folder => Path.GetDirectoryName(_path)!;

    /// <summary>The last lines of the log, oldest first. Empty if it can't be read.</summary>
    public IReadOnlyList<string> Tail(int count)
    {
        lock (_gate)
        {
            try { return File.Exists(_path) ? File.ReadLines(_path).TakeLast(count).ToList() : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }
    }

    /// <summary>Read before this launch is written, so it describes the session before.</summary>
    public bool LastSessionCrashed()
    {
        lock (_gate)
        {
            try { return File.Exists(_path) && CoreVision.Engine.CrashLog.LastSessionCrashed(File.ReadLines(_path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Move(_path, _path + ".old", true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // logging must never take the screen down
        }
    }
}
