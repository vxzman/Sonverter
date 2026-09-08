using System.Reflection;
using System.Runtime.InteropServices;

namespace Sonverter;

/// <summary>应用版本与编译环境信息。</summary>
public static class VersionInfo
{
    public const string ProductName = "Sonverter";

    public static string Version
        => typeof(VersionInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string BuildTimestamp
        => GetMetadata("BuildTimestamp") ?? "unknown";

    public static string DotnetVersion
        => Environment.Version.ToString();

    public static string Runtime
        => RuntimeInformation.FrameworkDescription;

    public static string Platform
        => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    public static string ProcessArchitecture
        => RuntimeInformation.ProcessArchitecture.ToString();

    public static string GetMetadata(string key)
        => typeof(VersionInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;

    public static void Print()
    {
        Console.WriteLine($"{ProductName} {Version}");
        Console.WriteLine($"Build time: {BuildTimestamp}");
        Console.WriteLine($".NET: {DotnetVersion}");
        Console.WriteLine($"Runtime: {Runtime}");
        Console.WriteLine($"Platform: {Platform}");
        Console.WriteLine($"Process architecture: {ProcessArchitecture}");
    }
}
