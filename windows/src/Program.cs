using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace Nimbus;

/// <summary>Command-line switches: --quit (close the running Nimbus) and --test (the automated Windows test:
/// keeps the panel open and listens for commands in %APPDATA%\Nimbus\test-commands.txt, see TestHook).</summary>
internal sealed record LaunchOptions(bool Test, bool Quit)
{
    public static LaunchOptions Parse(string[] args) => new(
        args.Contains("--test", StringComparer.OrdinalIgnoreCase),
        args.Contains("--quit", StringComparer.OrdinalIgnoreCase));
}

internal static class Program
{
    private const string MutexName = @"Local\Nimbus.SingleInstance";
    private const string OpenEventName = @"Local\Nimbus.Open";
    private const string QuitEventName = @"Local\Nimbus.Quit";
    // Microsoft's Evergreen WebView2 Runtime bootstrapper (needed on some Windows 10 PCs).
    private const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    [STAThread]
    private static void Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        // Single instance: a second launch just asks the running Nimbus to open its panel (or to quit).
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var firstInstance);
        if (!firstInstance)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(options.Quit ? QuitEventName : OpenEventName);
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                signal.Set();
            }
            catch (Exception ex)
            {
                Store.Log($"Could not signal the running Nimbus: {ex.Message}");
            }
            return;
        }
        if (options.Quit) return;   // nothing running, nothing to quit

        // Created straight away so a second launch can always reach this one.
        using var openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
        using var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Store.Log($"Unhandled: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Store.Log($"Fatal: {e.ExceptionObject}");

        if (!WebView2Installed())
        {
            var answer = MessageBox.Show(
                "Nimbus needs the Microsoft Edge WebView2 Runtime, which is missing on this PC.\n\n" +
                "It is built into Windows 11 and is a free, small download from Microsoft for Windows 10.\n\n" +
                "Open the download page now?",
                "Nimbus", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(WebView2Download) { UseShellExecute = true });
            return;
        }

        Store.Log($"Nimbus {Application.ProductVersion} starting" + (options.Test ? " (test mode)" : ""));
        var context = new TrayContext(options);
        var openWait = ThreadPool.RegisterWaitForSingleObject(
            openSignal, (_, _) => context.OpenFromOutside(), null, Timeout.Infinite, executeOnlyOnce: false);
        var quitWait = ThreadPool.RegisterWaitForSingleObject(
            quitSignal, (_, _) => context.QuitFromOutside(), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(context);

        openWait.Unregister(null);
        quitWait.Unregister(null);
        GC.KeepAlive(mutex);
    }

    private static bool WebView2Installed()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Store.Log($"WebView2 check failed: {ex.Message}");
            return true;   // let the real start report a clearer error
        }
    }
}
