using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace Lamha.App;

/// <summary>The entry point: Velopack's install hooks and the single-instance check run before any XAML loads.</summary>
public static class Program
{
    /// <summary>One Lamha per user; a second start hands over to the first (e.g. "open Lamha" from the Start menu).</summary>
    private const string InstanceKey = "Lamha-3";

    [STAThread]
    public static int Main(string[] args)
    {
        // Install, update and uninstall hooks; exits by itself when Velopack started the process for one of them.
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            // Off this STA thread: waiting on it here could deadlock the redirection.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        var options = AppOptions.FromArgs(args);
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, instance);
        });
        return 0;
    }
}
