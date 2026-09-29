using Microsoft.Extensions.Logging;

namespace LanguageLab.Tests.Fakes;

/// <summary>
/// Keeps every log line, formatted, so a test can check what was logged — and what was not. An
/// exception passed to the logger is appended to its line, so a leak through it is caught too.
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, exception is null ? formatter(state, exception) : $"{formatter(state, exception)} | {exception}"));
}
