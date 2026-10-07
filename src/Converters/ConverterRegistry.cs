namespace Sonverter.Converters;

using System.Text.Json.Nodes;

/// <summary>转换器注册中心，自动注册并管理所有转换器。</summary>
public static class ConverterRegistry
{
    private static readonly Dictionary<string, Func<JsonObject?, BaseConverter>> Converters = new();

    /// <summary>注册转换器（工厂委托，Native AOT 友好，不依赖反射）。</summary>
    public static void Register<T>(Func<JsonObject?, T> factory) where T : BaseConverter
    {
        var instance = factory(null);
        Converters[instance.Name] = factory;
    }

    /// <summary>获取转换器实例；不存在时抛出 <see cref="KeyNotFoundException"/>。</summary>
    public static BaseConverter Get(string name, JsonObject? config = null)
    {
        if (!Converters.TryGetValue(name, out var type))
            throw new KeyNotFoundException(
                $"转换器 '{name}' 不存在，可用的转换器：{string.Join(", ", Converters.Keys)}");

        return type(config);
    }

    /// <summary>获取所有已注册的转换器名称。</summary>
    public static List<string> ListNames() => Converters.Keys.ToList();

    /// <summary>获取所有转换器的信息。</summary>
    public static List<ConverterInfo> ListInfo()
        => Converters.Values
            .Select(factory => factory(null))
            .Select(c => c.GetInfo())
            .ToList();
}
