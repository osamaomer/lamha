using System.Globalization;

namespace Lamha.App.Services;

/// <summary>
/// A small log file, %LOCALAPPDATA%\Lamha Data\logs\lamha.log (for every profile), kept under 1 MB (the previous one is kept as lamha.1.log).
/// Same rule as Lamha 2: what happened, error type names and lengths; never the user's text, keys or clipboard.
/// Phase 2 replaces it with Microsoft.Extensions.Logging writing the same file.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly Lock Gate = new();
    private static readonly string Folder = Path.Combine(DataFolder.Default, "logs");

    public static string FilePath { get; } = Path.Combine(Folder, "lamha.log");

    public static void Info(string message) => Write("info", message);

    /// <summary>An error: its type and where it happened, never its message (a message can quote the user's text).</summary>
    public static void Error(string where, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Write("error", $"{where}: {ex.GetType().FullName} 0x{ex.HResult:X8}{Environment.NewLine}{ex.StackTrace}");
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(FilePath, Path.Combine(Folder, "lamha.1.log"), overwrite: true);
                }

                var time = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                File.AppendAllText(FilePath, $"{time} [{level}] {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // A log that can't be written must never stop Lamha.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
