namespace Sonverter.Converters;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Singbox 出口节点转换器：
/// 基于模板中的 include/exclude 正则表达式动态匹配节点、构建分组，
/// 执行级联剪枝（Cascade Pruning）与 default 容错，并保留节点的原始 Tag。
/// </summary>
public class SingboxConverter : BaseConverter
{
    public override string Name => "singbox";

    public override string Description => "Singbox 节点转换 - 基于模板正则自动匹配与级联分组";

    public override string[] SupportedExtensions { get; } = [".json"];

    public SingboxConverter(JsonObject? config = null) : base(config)
    {
    }

    public override object Convert(JsonNode data)
    {
        var rawNodes = ExtractRawNodes(data);

        // 1. 获取模板中定义的 outbounds 骨架
        var templateOutbounds = (Config["outbounds"] as JsonArray)?.DeepClone() as JsonArray
            ?? throw new InvalidOperationException("配置中缺少 'outbounds' 模板定义");

        var deletedTags = new HashSet<string>(StringComparer.Ordinal);
        var activeGroups = new List<JsonObject>();

        // 2. 遍历所有模板组，对带有 include 的组执行正则匹配并填充节点
        foreach (var item in templateOutbounds)
        {
            if (item is not JsonObject group)
                continue;

            var tag = group["tag"]?.GetValue<string>() ?? string.Empty;
            string? includePattern = null;
            if (group["include"] is JsonValue incVal && incVal.TryGetValue<string>(out var incStr) && !string.IsNullOrWhiteSpace(incStr))
            {
                includePattern = incStr;
            }

            if (includePattern is not null)
            {
                var excludePattern = group["exclude"] is JsonValue excVal
                    && excVal.TryGetValue<string>(out var excStr)
                    ? excStr
                    : null;

                var matchedTags = MatchNodes(rawNodes, includePattern, excludePattern);

                // 移除编译期指令 include 与 exclude
                group.Remove("include");
                group.Remove("exclude");

                if (matchedTags.Count == 0)
                {
                    // 命中数为 0，标记为删除组
                    if (!string.IsNullOrEmpty(tag))
                        deletedTags.Add(tag);
                    continue;
                }

                // 填充匹配成功的节点 Tag
                var outboundsArr = new JsonArray();
                foreach (var nodeTag in matchedTags)
                    outboundsArr.Add((JsonNode)nodeTag);
                group["outbounds"] = outboundsArr;

                activeGroups.Add(group);
            }
            else
            {
                // 无 include 的组（如全局选择器、直连等），保留待后续级联剪枝
                activeGroups.Add(group);
            }
        }

        // 3. 对上层嵌套组执行级联剪枝 (Cascade Pruning)
        foreach (var group in activeGroups)
        {
            if (group["outbounds"] is JsonArray outboundsArr)
            {
                // 剔除已被删除的组 Tag
                var filtered = new JsonArray();
                foreach (var node in outboundsArr)
                {
                    if (node is JsonValue val && val.TryGetValue<string>(out var targetTag))
                    {
                        if (!deletedTags.Contains(targetTag))
                            filtered.Add((JsonNode)targetTag);
                    }
                    else if (node is not null)
                    {
                        filtered.Add(node.DeepClone());
                    }
                }

                // 4. default 字段容错：若原 default 项已被删除，降级为第 1 项
                if (group["default"] is JsonValue defVal && defVal.TryGetValue<string>(out var defaultTag))
                {
                    if (deletedTags.Contains(defaultTag))
                    {
                        if (filtered.Count > 0 && filtered[0] is JsonValue firstVal && firstVal.TryGetValue<string>(out var firstTag))
                            group["default"] = firstTag;
                        else
                            group.Remove("default");
                    }
                }

                // 5. 极端空防御：如果组内所有引用的组全被删除，注入 "🌐直连"
                if (filtered.Count == 0 && (group["type"]?.GetValue<string>() is "selector" or "urltest"))
                {
                    filtered.Add((JsonNode)"🌐直连");
                }

                group["outbounds"] = filtered;
            }
        }

        // 6. 组装最终结果
        var finalOutbounds = new JsonArray();

        // 首先添加所有活跃策略组与固定出站
        foreach (var group in activeGroups)
            finalOutbounds.Add(group.DeepClone());

        // 末尾平铺所有原始物理节点（100% 保留原有属性与原始 Tag）
        foreach (var node in rawNodes)
            finalOutbounds.Add(node.DeepClone());

        // 保留输入数据中的其他顶级字段（如 endpoints、dns、route 等）
        var result = new JsonObject();
        if (data is JsonObject dataObj)
        {
            foreach (var (key, val) in dataObj)
            {
                if (key != "outbounds")
                    result[key] = val?.DeepClone();
            }
        }
        result["outbounds"] = finalOutbounds;

        return result;
    }

    /// <summary>从输入数据中提取物理节点列表。</summary>
    private static List<JsonObject> ExtractRawNodes(JsonNode data)
    {
        var rawNodes = new List<JsonObject>();
        JsonArray? sourceArr = null;

        if (data is JsonObject obj && obj["outbounds"] is JsonArray arr)
            sourceArr = arr;
        else if (data is JsonArray directArr)
            sourceArr = directArr;

        if (sourceArr is null)
            return rawNodes;

        var groupTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "selector", "urltest", "direct", "block", "dns"
        };

        foreach (var item in sourceArr)
        {
            if (item is not JsonObject node)
                continue;

            var tag = node["tag"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            var type = node["type"]?.GetValue<string>() ?? string.Empty;
            // 排除可能残留在旧配置中的控制组与直连组
            if (groupTypes.Contains(type))
                continue;

            rawNodes.Add(node);
        }

        return rawNodes;
    }

    /// <summary>使用正则表达式对节点 Tag 进行筛选。</summary>
    private static List<string> MatchNodes(List<JsonObject> rawNodes, string includePattern, string? excludePattern)
    {
        var matched = new List<string>();
        Regex? incRegex = null;
        Regex? excRegex = null;

        try
        {
            incRegex = new Regex(includePattern, RegexOptions.IgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"Invalid include regex '{includePattern}': {ex.Message}");
            return matched;
        }

        if (!string.IsNullOrWhiteSpace(excludePattern))
        {
            try
            {
                excRegex = new Regex(excludePattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warn($"Invalid exclude regex '{excludePattern}': {ex.Message}");
            }
        }

        foreach (var node in rawNodes)
        {
            var tag = node["tag"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (incRegex.IsMatch(tag))
            {
                if (excRegex is not null && excRegex.IsMatch(tag))
                    continue;

                matched.Add(tag);
            }
        }

        return matched;
    }
}
