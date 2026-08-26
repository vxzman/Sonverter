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

    /// <summary>
    /// 转换文件：读取输入文件、解析 JSON、调用 <see cref="Convert"/>、写出结果。
    /// 默认输出路径为 <c>{输入文件名去掉扩展名}_converted.json</c>。
    /// </summary>
    public virtual string ConvertFile(string inputPath, string? outputPath = null)
    {
        var text = File.ReadAllText(inputPath, System.Text.Encoding.UTF8);
        var data = System.Text.Json.Nodes.JsonNode.Parse(text)
            ?? throw new InvalidOperationException($"JSON 解析失败：{inputPath}");

        var result = Convert(data);
        if (result is not JsonNode node)
            throw new InvalidOperationException("转换结果不是有效的 JSON 数据");

        var fullInput = Path.GetFullPath(inputPath);
        var outPath = outputPath
            ?? Path.Combine(
                Path.GetDirectoryName(fullInput)!,
                $"{Path.GetFileNameWithoutExtension(fullInput)}_converted.json");

        File.WriteAllText(outPath, JsonHelper.Serialize(node), new System.Text.UTF8Encoding(false));
        return outPath;
    }

    /// <inheritdoc />
    public ConverterInfo GetInfo() => new(Name, Description, SupportedExtensions);
}
