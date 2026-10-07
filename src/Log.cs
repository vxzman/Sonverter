namespace Sonverter;

using Microsoft.Extensions.Logging;

/// <summary>
/// 统一控制台日志工具：单行、整洁、带有时间戳和级别颜色。
/// </summary>
public static class Log
{
    public static bool IsDebug { get; set; }

    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write("INFO ", message, ConsoleColor.Green);
    public static void Warn(string message) => Write("WARN ", message, ConsoleColor.Yellow);
    public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);
    public static void Debug(string message)
    {
        if (IsDebug)
            Write("DEBUG", message, ConsoleColor.DarkGray);
    }

    private static void Write(string level, string message, ConsoleColor color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        lock (SyncRoot)
        {
            Console.Write($"[{timestamp}] [");
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(level);
            Console.ForegroundColor = prev;
            Console.WriteLine($"] {message}");
        }
    }
}

/// <summary>
/// 针对 ASP.NET Core / Microsoft 框架的简洁日志 Provider：
/// 拦截框架自带的多行 Diagnostic 噪音，仅保留关键 Warning/Error 及应用日志。
/// </summary>
public sealed class CleanLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CleanLogger(categoryName);
    public void Dispose() { }
}

public sealed class CleanLogger(string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        // 屏蔽所有 Microsoft、System 的常规 Diagnostics、Lifetime、EndpointMiddleware 等噪音
        if (category.StartsWith("Microsoft", StringComparison.Ordinal) ||
            category.StartsWith("System", StringComparison.Ordinal))
        {
            return global::Sonverter.Log.IsDebug || logLevel >= LogLevel.Warning;
        }

        return global::Sonverter.Log.IsDebug || logLevel >= LogLevel.Information;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
            return;

        var shortCategory = GetShortCategory(category);
        var text = string.IsNullOrEmpty(shortCategory) ? message : $"{shortCategory}: {message}";
        if (exception != null)
        {
            text += Environment.NewLine + exception;
        }

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                global::Sonverter.Log.Debug(text);
                break;
            case LogLevel.Information:
                global::Sonverter.Log.Info(text);
                break;
            case LogLevel.Warning:
                global::Sonverter.Log.Warn(text);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                global::Sonverter.Log.Error(text);
                break;
        }
    }

    private static string GetShortCategory(string cat)
    {
        var lastDot = cat.LastIndexOf('.');
        return lastDot >= 0 && lastDot < cat.Length - 1 ? cat[(lastDot + 1)..] : cat;
    }
}
