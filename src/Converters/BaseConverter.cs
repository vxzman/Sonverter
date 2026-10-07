namespace Sonverter.Converters;

using System.Text.Json.Nodes;

/// <summary>转换器基类，所有转换器都继承它并实现统一接口。</summary>
public abstract class BaseConverter : IConverter
{
    /// <summary>转换器名称（子类必须定义）。</summary>
    public abstract string Name { get; }

    /// <summary>转换器描述（子类必须定义）。</summary>
    public abstract string Description { get; }

    /// <summary>支持的输入文件扩展名。</summary>
    public virtual string[] SupportedExtensions { get; } = [".json"];

    /// <summary>配置（默认空对象，可被 --config 覆盖）。</summary>
    public JsonObject Config { get; }

    protected BaseConverter(JsonObject? config = null)
    {
        Config = config ?? new JsonObject();
    }

    /// <inheritdoc />
    public abstract object Convert(JsonNode data);

    /// <summary>判断是否为 HTTP(S) URL。</summary>
    public static bool IsUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>从文件或 URL 读取文本内容。</summary>
    public static string ReadInputText(string inputPath)
    {
        if (IsUrl(inputPath))
        {
            Log.Info($"Fetching node list from URL: {inputPath}");
            using var handler = new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = delegate { return true; }
                }
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sonverter/1.0");
            return client.GetStringAsync(inputPath).GetAwaiter().GetResult();
        }

        return File.ReadAllText(inputPath, System.Text.Encoding.UTF8);
    }

    /// <summary>获取默认输出扩展名。</summary>
    protected virtual string GetDefaultExtension() => SupportedExtensions.FirstOrDefault() ?? ".json";

    /// <summary>
    /// 转换文件或 URL：读取输入、解析 JSON、调用 <see cref="Convert"/>、写出结果。
    /// </summary>
    public virtual string ConvertFile(string inputPath, string? outputPath = null)
    {
        var text = ReadInputText(inputPath);
        var data = JsonNode.Parse(text)
            ?? throw new InvalidOperationException($"JSON 解析失败：{inputPath}");

        var result = Convert(data);
        if (result is not JsonNode node)
            throw new InvalidOperationException("转换结果不是有效的 JSON 数据");

        string outPath;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            outPath = outputPath;
        }
        else if (IsUrl(inputPath))
        {
            var uri = new Uri(inputPath);
            var seg = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault();
            var name = !string.IsNullOrWhiteSpace(seg) ? seg : Name;
            outPath = Path.Combine(Directory.GetCurrentDirectory(), $"{name}_converted{GetDefaultExtension()}");
        }
        else
        {
            var fullInput = Path.GetFullPath(inputPath);
            outPath = Path.Combine(
                Path.GetDirectoryName(fullInput)!,
                $"{Path.GetFileNameWithoutExtension(fullInput)}_converted{GetDefaultExtension()}");
        }

        File.WriteAllText(outPath, JsonHelper.Serialize(node), new System.Text.UTF8Encoding(false));
        return outPath;
    }

    /// <inheritdoc />
    public ConverterInfo GetInfo() => new(Name, Description, SupportedExtensions);
}
