namespace Lamha.App.Services;

/// <summary>
/// Where the user's data lives: <c>%LOCALAPPDATA%\Lamha Data</c> (decision 0006), apart from Velopack's install folder
/// <c>%LOCALAPPDATA%\Lamha</c>, which an uninstall deletes. The log is there too (<c>logs\</c>), for every profile.
/// </summary>
public static class DataFolder
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string Default { get; } = Path.Combine(LocalAppData, "Lamha Data");

    /// <summary>The data folder for this run: <paramref name="profile"/> when given, otherwise the default one.</summary>
    public static string For(string? profile)
    {
        if (profile is not null)
        {
            return profile;
        }

        MoveFromInstallFolder();
        return Default;
    }

    /// <summary>
    /// The phase 3 builds kept lamha.db in the install folder: move it (with its WAL files) once, so trying those builds
    /// isn't lost. Only builds from source ever wrote there; the importer from Lamha 2 is another matter (phase 6).
    /// </summary>
    private static void MoveFromInstallFolder()
    {
        var old = Path.Combine(LocalAppData, "Lamha", "lamha.db");
        var db = Path.Combine(Default, "lamha.db");
        if (!File.Exists(old) || File.Exists(db))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Default);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(old + suffix))
                {
                    File.Move(old + suffix, db + suffix);
                }
            }

            AppLog.Info("data moved from the install folder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("moving the data", ex);
        }
    }
}
