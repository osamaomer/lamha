using global::Windows.Win32;

namespace Lamha.Windows.Shell;

/// <summary>Lamha's own memory.</summary>
public static class ProcessMemory
{
    /// <summary>
    /// Gives Lamha's idle pages back to Windows (they stay in its standby list, so using them again is cheap).
    /// For when every window has closed and Lamha is back to waiting in the tray: the memory a window used stops
    /// counting against the PC until the next window opens. Nothing is freed that Lamha still holds (commit is unchanged).
    /// </summary>
    public static void TrimIdle() =>
        PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
}
