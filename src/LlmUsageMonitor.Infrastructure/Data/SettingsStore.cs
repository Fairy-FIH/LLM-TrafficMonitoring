using System.Text.Json;
using LlmUsageMonitor.Core.Abstractions;
using LlmUsageMonitor.Core.Models;

namespace LlmUsageMonitor.Infrastructure.Data;

/// <summary>Persists <see cref="AppSettings"/> as an AES-encrypted blob on disk.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? Changed;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path))
            {
                Current = new AppSettings();
                return Current;
            }

            var raw = await File.ReadAllTextAsync(_path, ct);
            var json = _protector.IsProtected(raw) ? _protector.Unprotect(raw) : raw;
            if (string.IsNullOrWhiteSpace(json))
            {
                Current = new AppSettings();
                return Current;
            }

            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Current;
        }
        catch
        {
            Current = new AppSettings();
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Current = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var blob = _protector.Protect(json);
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, blob, ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, settings);
    }
}
