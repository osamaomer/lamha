using Lamha.App.Services;
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
        // Uninstalling takes Lamha's notification name and links out of the registry (the user's data folder stays).
        // Not Velopack's own install of a downloaded version at start: in a second start (the Start menu, a
        // notification's click) it would close the Lamha already running without letting it save (tried 2026-10-26).
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => App.Notifications(profile: null).Unregister())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (AppOptions.IsReader(args))
        {
            // A book's reader: a Lamha of its own beside the one in the tray, so closing the book gives all its memory
            // back (decision 0022). It never takes the single-instance key nor installs a pending update.
            Application.Start(callback =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App(AppOptions.FromArgs(args), instance: null);
            });
            return 0;
        }

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            // Off this STA thread: waiting on it here could deadlock the redirection.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        var options = AppOptions.FromArgs(args);
        // A version downloaded before Lamha last ended without installing it (Windows shut down): installed now, as the
        // only Lamha running, and Lamha starts again with the same arguments.
        if (new VelopackUpdates(options.Updates, args).InstallPending())
        {
            return 0;
        }

        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, instance);
        });
        return 0;
    }
}
