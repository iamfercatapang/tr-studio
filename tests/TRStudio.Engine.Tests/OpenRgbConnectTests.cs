using TRStudio.Engine;
using Xunit;

public sealed class OpenRgbConnectTests
{
    /// <summary>
    /// TR Studio asks every few seconds while OpenRGB is closed. Each ask must end cleanly: a connect left
    /// running fails later on the finalizer thread, which filled the log with a "Background error" every 5 seconds.
    /// </summary>
    [Fact]
    public void Asking_while_openrgb_is_closed_leaves_no_failure_behind()
    {
        if (PortInUse()) return; // OpenRGB is open on this PC, so there is no closed port to ask
        var unobserved = 0;
        void Count(object? sender, UnobservedTaskExceptionEventArgs e) => Interlocked.Increment(ref unobserved);
        TaskScheduler.UnobservedTaskException += Count;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
                Assert.Throws<LightingUnavailableException>(() => OpenRgbLightingClient.Connect());
            Thread.Sleep(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally { TaskScheduler.UnobservedTaskException -= Count; }

        Assert.Equal(0, unobserved);
    }

    static bool PortInUse()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(OpenRgbLightingClient.Host, OpenRgbLightingClient.Port);
            if (connect.Wait(3000)) return true;
            connect.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted); // this check must not leave a failure behind either
            return false;
        }
        catch (AggregateException) { return false; }
    }
}
