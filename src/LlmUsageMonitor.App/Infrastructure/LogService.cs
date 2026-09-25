using System.Collections.ObjectModel;
using System.Windows;

namespace LlmUsageMonitor.App.Infrastructure;

/// <summary>In-app log sink surfaced on the Logs page.</summary>
public sealed class LogService
{
    private const int MaxEntries = 1000;
    private readonly object _gate = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public event EventHandler<LogEntry>? Appended;

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Warn(string message) => Add(LogLevel.Warn, message);
    public void Error(string message) => Add(LogLevel.Error, message);

    public void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        void Append()
        {
            lock (_gate)
            {
                Entries.Insert(0, entry);
                while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
            }
            Appended?.Invoke(this, entry);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Append();
        else dispatcher.BeginInvoke(Append);
    }
}

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss");
}
