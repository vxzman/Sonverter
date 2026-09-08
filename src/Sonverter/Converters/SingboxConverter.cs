namespace Sonverter.Converters;

using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

/// <summary>
/// Singbox 出口节点转换器：为 tag 添加 emoji 前缀，并按国家/地区整理到模板中。
/// 逻辑与 Python 版 converters/singbox_converter.py 保持一致。
/// </summary>
public class SingboxConverter : BaseConverter
{
    public override string Name => "singbox";

    public override string Description => "Singbox 节点转换 - 添加 emoji 前缀并按国家/地区分组";

    public override string[] SupportedExtensions { get; } = [".json"];

    /// <summary>需要排除的技术词汇。</summary>
    private static readonly HashSet<string> ExcludeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIRECT", "PROXY", "NODE", "SERVER", "TEST", "AWS", "AZURE", "GCP", "ALIYUN", "TENCENT",
    };

    private readonly Dictionary<string, string> _countryMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sortedKeys = new();
    private readonly List<(string Name, string Label)> _countryNames = new();
    private readonly List<string> _selectList = new();
    private readonly List<string> _orderedLabels = new();
    private readonly string _hkUrl;
    private readonly string _defaultUrl;
    private readonly string _otherTag;
    private readonly string _directTag;

    public SingboxConverter(JsonObject? config = null) : base(config)
    {
        if (Config["country_map"] is JsonObject countryMap)
        {
            foreach (var (key, value) in countryMap)
                if (value is JsonValue v && v.TryGetValue<string>(out var label))
                    _countryMap[key] = label;

            // 节点 tag 也常使用“🇭🇰 香港 01”这类中文名称，而不是 HK 代码。
            // 同时支持配置中的完整名称及“中国香港”这类名称的简写。
            foreach (var label in _countryMap.Values.Distinct(StringComparer.Ordinal))
            {
                var name = Regex.Replace(label, "[^一-鿿]", "");
                if (name.Length == 0)
                    continue;

                AddCountryName(name, label);
                if (name.StartsWith("中国", StringComparison.Ordinal) && name.Length > 2)
                    AddCountryName(name[2..], label);
            }
        }

        _sortedKeys = _countryMap.Keys
            .OrderByDescending(k => k.Length)
            .ToList();
        _countryNames.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));

        if (Config["select_list"] is JsonArray selectList)
            _selectList = selectList
                .Select(x => x?.GetValue<string>() ?? string.Empty)
                .ToList();

        if (Config["ordered_labels"] is JsonArray orderedLabels)
            _orderedLabels = orderedLabels
                .Select(x => x?.GetValue<string>() ?? string.Empty)
                .ToList();

        _hkUrl = GetString("hk_url", "https://cp.cloudflare.com/generate_204");
        _defaultUrl = GetString("default_url", "https://www.gstatic.com/generate_204");
        _otherTag = GetString("other_tag", "🗺️其他国家");
        _directTag = GetString("direct_tag", "🔗DIRECT");
    }

    private string GetString(string key, string fallback)
        => Config[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    private void AddCountryName(string name, string label)
    {
        if (_countryNames.Any(x => x.Name.Equals(name, StringComparison.Ordinal)
                                   && x.Label.Equals(label, StringComparison.Ordinal)))
            return;
        _countryNames.Add((name, label));
    }

    /// <summary>从 tag 中检测国家/地区，返回 emoji+名称（找不到时返回其他国家）。</summary>
    private string DetectCountry(string tag)
    {
        var up = tag.ToUpperInvariant();

        // 策略 0：排除技术词汇
        foreach (var exclude in ExcludeWords)
        {
            if (up == exclude
                || up.StartsWith(exclude + "-", StringComparison.Ordinal)
                || up.StartsWith(exclude + "_", StringComparison.Ordinal)
                || up.StartsWith(exclude + " ", StringComparison.Ordinal))
                return _otherTag;
        }

        // 策略 1：匹配中文国家/地区名称（兼容 emoji 和名称之间的空格）。
        var chineseTag = Regex.Replace(tag, "[^一-鿿]", "");
        foreach (var (name, label) in _countryNames)
            if (chineseTag.Contains(name, StringComparison.Ordinal))
                return label;

        // 策略 2：优先匹配 tag 开头的国家代码
        foreach (var key in _sortedKeys)
        {
            if (up.StartsWith(key + "-", StringComparison.Ordinal)
                || up.StartsWith(key + "_", StringComparison.Ordinal)
                || up.StartsWith(key + " ", StringComparison.Ordinal))
                return _countryMap[key];
        }

        // 策略 3：匹配完整的单词
        foreach (var key in _sortedKeys)
        {
            if (ExcludeWords.Contains(key))
                continue;
            var pattern = @"(^|[-_\s])" + Regex.Escape(key) + @"([-_\s]|$)";
            if (Regex.IsMatch(up, pattern))
                return _countryMap[key];
        }

        // 策略 4：子字符串匹配
        foreach (var key in _sortedKeys)
        {
            if (ExcludeWords.Contains(key))
                continue;
            if (up.Contains(key, StringComparison.Ordinal))
                return _countryMap[key];
        }

        return _otherTag;
    }

    /// <summary>为 tag 添加 emoji 前缀。</summary>
    private string EmojiizeTag(string tag) => $"{DetectCountry(tag)} {tag}";

    /// <summary>深拷贝数据并转换所有 tag。</summary>
    private JsonObject ConvertTags(JsonNode data)
    {
        if (data.DeepClone() is not JsonObject root)
            return new JsonObject { ["outbounds"] = new JsonArray() };

        if (root["outbounds"] is JsonArray outbounds)
        {
            foreach (var item in outbounds)
            {
                if (item is not JsonObject node)
                    continue;
                if (node["tag"] is JsonValue tagValue && tagValue.TryGetValue<string>(out var tag))
                    node["tag"] = EmojiizeTag(tag);
            }
        }

        return root;
    }

    /// <summary>将节点按国家/地区分组。</summary>
    private (Dictionary<string, List<string>> Groups, List<string> OtherNodes) GroupNodes(JsonObject converted)
    {
        var groups = new Dictionary<string, List<string>>();
        var otherNodes = new List<string>();
        var labels = _countryMap.Values.Distinct().ToList();

        foreach (var item in converted["outbounds"] as JsonArray ?? new JsonArray())
        {
            if (item is not JsonObject node)
                continue;
            if (node["tag"] is not JsonValue tagValue || !tagValue.TryGetValue<string>(out var tag))
                continue;

            var label = labels.FirstOrDefault(tag.StartsWith);
            if (label is not null)
            {
                if (!groups.TryGetValue(label, out var list))
                    groups[label] = list = new List<string>();
                list.Add(tag);
            }
            else
            {
                otherNodes.Add(tag);
            }
        }

        return (groups, otherNodes);
    }

    /// <summary>构建整理后的 outbounds 模板。</summary>
    private JsonObject BuildTemplate(JsonObject converted)
    {
        var (groups, otherNodes) = GroupNodes(converted);
        var outbounds = new JsonArray();
        var availableCountries = new List<string>();

        // 配置中定义的国家/地区（有节点时保留）
        foreach (var label in _orderedLabels)
            if (groups.TryGetValue(label, out var nodes) && nodes.Count > 0)
                availableCountries.Add(label);

        // 新增国家/地区：3 个及以上节点时自动加入
        var newCountries = new List<string>();
        foreach (var (label, nodes) in groups)
        {
            if (nodes.Count >= 3 && !_orderedLabels.Contains(label))
            {
                newCountries.Add(label);
                availableCountries.Add(label);
            }
        }

        // 少于 3 个节点的国家/地区归入"其他国家"
        var otherCountryNodes = new List<string>();
        foreach (var (label, nodes) in groups)
            if (nodes.Count < 3 && !_orderedLabels.Contains(label))
                otherCountryNodes.AddRange(nodes);
        otherCountryNodes.AddRange(otherNodes);

        var hasOther = otherCountryNodes.Count > 0;
        if (hasOther)
            availableCountries.Add(_otherTag);

        var selectOutbounds = availableCountries.Append(_directTag).ToList();

        // SELECT
        outbounds.Add((JsonNode)new JsonObject
        {
            ["type"] = "selector",
            ["tag"] = "🚀SELECT",
            ["outbounds"] = ToJsonArray(selectOutbounds),
        });

        // FINAL
        outbounds.Add((JsonNode)new JsonObject
        {
            ["type"] = "selector",
            ["tag"] = "🏁FINAL",
            ["outbounds"] = ToJsonArray(["🚀SELECT", _directTag]),
        });

        // urltest for ordered_labels
        foreach (var label in _orderedLabels)
            if (groups.TryGetValue(label, out var nodes) && nodes.Count > 0)
                outbounds.Add((JsonNode)Urltest(label, nodes));

        // urltest for new countries
        foreach (var label in newCountries)
            if (groups.TryGetValue(label, out var nodes) && nodes.Count > 0)
                outbounds.Add((JsonNode)Urltest(label, nodes));

        // Other countries
        if (hasOther)
            outbounds.Add((JsonNode)new JsonObject
            {
                ["type"] = "selector",
                ["tag"] = _otherTag,
                ["outbounds"] = ToJsonArray(otherCountryNodes),
            });

        // DIRECT
        outbounds.Add((JsonNode)new JsonObject
        {
            ["type"] = "direct",
            ["tag"] = _directTag,
        });

        return new JsonObject { ["outbounds"] = outbounds };
    }

    private JsonObject Urltest(string label, List<string> nodes)
    {
        var url = Random.Shared.Next(2) == 0 ? _hkUrl : _defaultUrl;
        return new JsonObject
        {
            ["type"] = "urltest",
            ["tag"] = label,
            ["outbounds"] = ToJsonArray(nodes),
            ["url"] = url,
        };
    }

    private static JsonArray ToJsonArray(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add((JsonNode)item);
        return array;
    }

    /// <summary>
    /// 转换数据：分组模板（SELECT/urltest/其他国家）在前，添加 emoji 前缀后的节点列表在后。
    /// </summary>
    public override object Convert(JsonNode data)
    {
        var converted = ConvertTags(data);
        var organized = BuildTemplate(converted);

        var outbounds = new JsonArray();
        foreach (var item in organized["outbounds"]!.AsArray())
            outbounds.Add(item?.DeepClone());
        foreach (var item in converted["outbounds"]!.AsArray())
            outbounds.Add(item?.DeepClone());

        return new JsonObject { ["outbounds"] = outbounds };
    }
}
