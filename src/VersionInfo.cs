using System.Reflection;
using System.Runtime.InteropServices;

namespace Sonverter;

/// <summary>应用构建日期与运行环境信息。</summary>
public static class VersionInfo
{
    public const string ProductName = "Sonverter";

    public static string BuildDate
    {
        get
        {
            var meta = GetMetadata("BuildDate");
            if (!string.IsNullOrEmpty(meta))
                return meta;

            var infoVersion = typeof(VersionInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
                return infoVersion.Split('+')[0];

            return DateTime.UtcNow.ToString("yyyyMMdd1");
        }
    }

    public static string Version => BuildDate;
    public static string BuildTimestamp => BuildDate;

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
        Console.WriteLine($"{ProductName} ({BuildDate})");
        Console.WriteLine($".NET: {DotnetVersion}");
        Console.WriteLine($"Runtime: {Runtime}");
        Console.WriteLine($"Platform: {Platform}");
        Console.WriteLine($"Process architecture: {ProcessArchitecture}");
    }
}
