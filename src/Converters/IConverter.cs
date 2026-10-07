namespace Sonverter.Converters;

using System.Text.Json.Nodes;

/// <summary>转换器统一接口。</summary>
public interface IConverter
{
    /// <summary>转换器名称（用于命令行 --converter）。</summary>
    string Name { get; }

    /// <summary>转换器描述。</summary>
    string Description { get; }

    /// <summary>支持的输入文件扩展名。</summary>
    string[] SupportedExtensions { get; }

    /// <summary>转换数据。</summary>
    object Convert(JsonNode data);

    /// <summary>转换文件。</summary>
    string ConvertFile(string inputPath, string? outputPath = null);

    /// <summary>获取转换器信息。</summary>
    ConverterInfo GetInfo();
}

/// <summary>转换器信息。</summary>
public record ConverterInfo(string Name, string Description, string[] SupportedExtensions);
