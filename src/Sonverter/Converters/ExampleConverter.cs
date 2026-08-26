namespace Sonverter.Converters;

using System.Text.Json.Nodes;

/// <summary>示例转换器，展示如何添加新的转换格式。</summary>
public class ExampleConverter : BaseConverter
{
    public override string Name => "example";

    public override string Description => "示例转换器 - 展示如何添加新格式";

    public override string[] SupportedExtensions { get; } = [".txt", ".json"];

    public ExampleConverter(JsonObject? config = null) : base(config)
    {
    }

    public override object Convert(JsonNode data)
    {
        return new JsonObject
        {
            ["message"] = "这是示例转换器的输出",
            ["input_type"] = data.GetType().Name,
            ["config"] = Config.DeepClone(),
        };
    }
}
