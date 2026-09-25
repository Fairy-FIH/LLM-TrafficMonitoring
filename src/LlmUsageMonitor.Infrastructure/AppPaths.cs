namespace LlmUsageMonitor.Infrastructure;

/// <summary>Resolves on-disk locations for the portable or installed app.</summary>
public sealed class AppPaths
{
    public AppPaths(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlmUsageMonitor");
        Directory.CreateDirectory(BaseDirectory);
    }

    public string BaseDirectory { get; }

    public string DatabasePath => Path.Combine(BaseDirectory, "data.db");
    public string SettingsPath => Path.Combine(BaseDirectory, "settings.dat");
    public string KeyPath => Path.Combine(BaseDirectory, "key.bin");
    public string LogPath => Path.Combine(BaseDirectory, "logs", "app.log");
    public string ExportDirectory
    {
        get
        {
            var dir = Path.Combine(BaseDirectory, "exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
