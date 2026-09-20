using System.Windows.Forms;

namespace OmnisRouter.Tray;

/// <summary>
/// Entry point for the Windows tray watcher. Enforces a single instance per user session (a second
/// launch signals the running one to show its popup, then exits — FR-005), installs a WinForms
/// synchronization context for marshaling engine events, and runs the tray.
/// </summary>
internal static class Program
{
    // "Local\" scopes the names to the current session, so a second logged-in user gets their own.
    private const string MutexName = @"Local\OmnisRouter.Collect.Tray";
    private const string ShowEventName = @"Local\OmnisRouter.Collect.Show";

    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isNew);
        if (!isNew)
        {
            // Already running: nudge the live instance to surface its popup, then exit.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }

            return 0;
        }

        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Pin the app to the classic light rendering. This machine has apps set to light but the system
        // theme dark; .NET 10 was dark-painting the bordered dialogs from the system setting, which put
        // the settings-window checkbox text (dark) on a dark-painted background and made it unreadable.
        // Forcing Classic keeps every window light, matching the user's light-apps preference, so the
        // dialogs' dark-on-light text is visible. The status popup sets its own explicit colours and is
        // unaffected. SetColorMode is still experimental (WFO5001), hence the suppression.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Classic);
#pragma warning restore WFO5001

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var context = new TrayContext();
        context.ListenForShowRequests(showRequested);

        Application.Run(context);
        return 0;
    }
}
