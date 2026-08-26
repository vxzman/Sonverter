namespace Sonverter;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// 默认配置：优先加载嵌入式资源 template.json，
/// 用户通过 --config 提供的文件会在此基础上浅合并覆盖。
/// </summary>
public static class DefaultConfig
{
    private const string ResourceName = "Sonverter.template.json";

    /// <summary>
    /// 加载配置。configPath 为 null 或文件不存在时使用内置 template.json。
    /// 合并语义与 Python 版一致：顶层 dict 按 key 合并，其余整体替换。
    /// </summary>
    public static JsonObject Load(string? configPath)
    {
        var defaults = LoadEmbedded();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return defaults;

        try
        {
            var overrides = ParseObject(File.ReadAllText(configPath, System.Text.Encoding.UTF8));
            return overrides is null ? defaults : Merge(defaults, overrides);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"警告：配置文件加载失败 {configPath}: {e.Message}");
            return defaults;
        }
    }

    private static JsonObject LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入式资源 {ResourceName}");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return ParseObject(reader.ReadToEnd())
            ?? throw new InvalidOperationException("默认配置解析失败");
    }

    /// <summary>
    /// 解析 JSON 并重建为 JsonObject。
    /// 与 Python json.loads 语义一致：重复键后者覆盖、保留首次出现的位置。
    /// System.Text.Json 的 JsonObject 对重复键会抛异常，因此先经 JsonDocument 手动重建。
    /// </summary>
    private static JsonObject? ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ConvertElement(document.RootElement) as JsonObject;
    }

    private static JsonNode? ConvertElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    var value = ConvertElement(property.Value);
                    if (obj.ContainsKey(property.Name))
                        obj[property.Name] = value;
                    else
                        obj.Add(property.Name, value);
                }
                return obj;
            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray())
                    array.Add(ConvertElement(item));
                return array;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return JsonNode.Parse(element.GetRawText());
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static JsonObject Merge(JsonObject defaults, JsonObject overrides)
    {
        var result = defaults.DeepClone()!.AsObject();
        foreach (var (key, value) in overrides)
        {
            if (value is JsonObject overrideObj && result[key] is JsonObject baseObj)
            {
                foreach (var (innerKey, innerValue) in overrideObj)
                    baseObj[innerKey] = innerValue?.DeepClone();
            }
            else
            {
                result[key] = value?.DeepClone();
            }
        }
        return result;
    }
}
