namespace Sonverter.Converters;

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// dae 出口节点转换器：把订阅 URL 列表（每行 协议://具体配置#备注）转换为 dae 的 node 配置。
/// tag 规则：国家简写-协议简写-序号（如 hk-hy2-01），提取不到国家信息时用 other（如 other-vless-01）。
/// dae 的 tag 只支持英文，中文/emoji 备注解码后作为行尾注释保留。
/// </summary>
public class DaeConverter : BaseConverter
{
    public override string Name => "dae";

    public override string Description => "dae 节点转换 - 订阅 URL 列表转 node 配置（英文 tag + 备注注释）";

    public override string[] SupportedExtensions { get; } = [".txt"];

    /// <summary>URL 协议 → tag 中的协议简写。</summary>
    private static readonly Dictionary<string, string> ProtocolAbbr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vless"] = "vless",
        ["vmess"] = "vmess",
        ["hysteria2"] = "hy2",
        ["hy2"] = "hy2",
        ["hysteria"] = "hy",
        ["hy"] = "hy",
        ["tuic"] = "tuic",
        ["trojan"] = "trojan",
        ["shadowsocks"] = "ss",
        ["ss"] = "ss",
        ["ssr"] = "ssr",
        ["juicity"] = "juicity",
        ["wireguard"] = "wg",
        ["socks5"] = "socks5",
        ["socks"] = "socks5",
        ["http"] = "http",
        ["https"] = "https",
    };

    /// <summary>非两字母的国家别名 → 标准两字母代码。</summary>
    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USA"] = "us",
        ["AMERICA"] = "us",
        ["UK"] = "gb",
        ["JAPAN"] = "jp",
        ["KOREA"] = "kr",
        ["SINGAPORE"] = "sg",
        ["HONGKONG"] = "hk",
        ["TAIWAN"] = "tw",
        ["AUSTRALIA"] = "au",
        ["MACAU"] = "mo",
        ["MACAO"] = "mo",
    };

    /// <summary>内置默认国家映射（代码/中英文 → 标准代码）。</summary>
    private static readonly Dictionary<string, string> DefaultCountryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HK"] = "hk", ["HONGKONG"] = "hk", ["香港"] = "hk", ["中国香港"] = "hk",
        ["JP"] = "jp", ["JAPAN"] = "jp", ["日本"] = "jp", ["东京"] = "jp", ["大阪"] = "jp",
        ["TW"] = "tw", ["TAIWAN"] = "tw", ["台湾"] = "tw", ["中国台湾"] = "tw", ["台北"] = "tw",
        ["SG"] = "sg", ["SINGAPORE"] = "sg", ["新加坡"] = "sg", ["狮城"] = "sg",
        ["US"] = "us", ["USA"] = "us", ["AMERICA"] = "us", ["美国"] = "us", ["洛杉矶"] = "us", ["硅谷"] = "us",
        ["KR"] = "kr", ["KOREA"] = "kr", ["韩国"] = "kr", ["首尔"] = "kr",
        ["GB"] = "gb", ["UK"] = "gb", ["BRITAIN"] = "gb", ["ENGLAND"] = "gb", ["英国"] = "gb", ["伦敦"] = "gb",
        ["DE"] = "de", ["GERMANY"] = "de", ["德国"] = "de", ["法兰克福"] = "de",
        ["FR"] = "fr", ["FRANCE"] = "fr", ["法国"] = "fr", ["巴黎"] = "fr",
        ["CA"] = "ca", ["CANADA"] = "ca", ["加拿大"] = "ca", ["温哥华"] = "ca", ["多伦多"] = "ca",
        ["AU"] = "au", ["AUSTRALIA"] = "au", ["澳大利亚"] = "au", ["澳洲"] = "au", ["悉尼"] = "au",
        ["TR"] = "tr", ["TURKEY"] = "tr", ["土耳其"] = "tr", ["伊斯坦布尔"] = "tr",
        ["RU"] = "ru", ["RUSSIA"] = "ru", ["俄罗斯"] = "ru", ["莫斯科"] = "ru",
        ["IN"] = "in", ["INDIA"] = "in", ["印度"] = "in",
        ["MY"] = "my", ["MALAYSIA"] = "my", ["马来西亚"] = "my",
        ["TH"] = "th", ["THAILAND"] = "th", ["泰国"] = "th",
        ["VN"] = "vn", ["VIETNAM"] = "vn", ["越南"] = "vn",
        ["PH"] = "ph", ["PHILIPPINES"] = "ph", ["菲律宾"] = "ph",
        ["ID"] = "id", ["INDONESIA"] = "id", ["印尼"] = "id",
        ["NL"] = "nl", ["NETHERLANDS"] = "nl", ["荷兰"] = "nl", ["阿姆斯特丹"] = "nl",
        ["CH"] = "ch", ["SWITZERLAND"] = "ch", ["瑞士"] = "ch", ["苏黎世"] = "ch",
        ["SE"] = "se", ["SWEDEN"] = "se", ["瑞典"] = "se",
        ["NO"] = "no", ["NORWAY"] = "no", ["挪威"] = "no",
        ["FI"] = "fi", ["FINLAND"] = "fi", ["芬兰"] = "fi",
        ["IT"] = "it", ["ITALY"] = "it", ["意大利"] = "it", ["米兰"] = "it",
        ["ES"] = "es", ["SPAIN"] = "es", ["西班牙"] = "es", ["马德里"] = "es",
        ["BR"] = "br", ["BRAZIL"] = "br", ["巴西"] = "br",
        ["AR"] = "ar", ["ARGENTINA"] = "ar", ["阿根廷"] = "ar",
        ["MX"] = "mx", ["MEXICO"] = "mx", ["墨西哥"] = "mx",
        ["AE"] = "ae", ["UAE"] = "ae", ["阿联酋"] = "ae", ["迪拜"] = "ae",
        ["MO"] = "mo", ["MACAU"] = "mo", ["MACAO"] = "mo", ["澳门"] = "mo", ["中国澳门"] = "mo",
    };

    /// <summary>英文国家代码（按长度降序匹配，长别名优先）。</summary>
    private readonly List<(string Key, string Code)> _countryKeys = new();

    /// <summary>中文国家名（从标签提取，如 🇭🇰中国香港 → 中国香港/香港）。</summary>
    private readonly List<(string Name, string Code)> _countryNames = new();

    public DaeConverter(JsonObject? config = null) : base(config)
    {
        // 1. 初始化内置映射
        foreach (var (key, code) in DefaultCountryMap)
        {
            if (key.All(char.IsAsciiLetter))
            {
                _countryKeys.Add((key, code));
            }
            else
            {
                AddCountryName(key, code);
            }
        }

        // 2. 合并外部配置的 country_map（如有）
        if (Config["country_map"] is JsonObject countryMap)
        {
            foreach (var (key, value) in countryMap)
            {
                if (value is not JsonValue v || !v.TryGetValue<string>(out var label))
                    continue;

                var code = ToCountryCode(key);
                if (code is null)
                    continue;
                if (!_countryKeys.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    _countryKeys.Add((key, code));

                var name = Regex.Replace(label, "[^一-鿿]", "");
                if (name.Length == 0)
                    continue;
                AddCountryName(name, code);
                if (name.StartsWith("中国", StringComparison.Ordinal) && name.Length > 2)
                    AddCountryName(name[2..], code);
            }
        }

        _countryKeys.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        _countryNames.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));
    }

    private void AddCountryName(string name, string code)
    {
        if (_countryNames.Any(x => x.Name == name && x.Code == code))
            return;
        _countryNames.Add((name, code));
    }

    /// <summary>国家代码 key → 标准两字母代码；无法映射（自定义多词 key 等）返回 null。</summary>
    private static string? ToCountryCode(string key)
    {
        if (CountryAliases.TryGetValue(key, out var alias))
            return alias;
        if (key.Length == 2 && key.All(char.IsAsciiLetter))
            return key.ToLowerInvariant();
        return null;
    }

    /// <summary>
    /// 从备注中检测国家，返回标准两字母代码；找不到时返回 null（tag 用 other）。
    /// </summary>
    private string? DetectCountry(string remark)
    {
        if (remark.Length == 0)
            return null;

        // 去掉 emoji/空白等干扰字符，便于匹配（如 🇭🇰香港1 → 香港1）
        var normalized = Regex.Replace(remark, "[^a-zA-Z0-9_\\-一-鿿]", "");

        // 英文代码：开头匹配，后跟分隔符/数字/结尾才算，避免 IN 误匹配 Indonesia 等
        var upper = normalized.ToUpperInvariant();
        foreach (var (key, code) in _countryKeys)
        {
            if (!upper.StartsWith(key, StringComparison.Ordinal))
                continue;
            if (upper.Length == key.Length || !char.IsAsciiLetter(upper[key.Length]))
                return code;
        }

        // 中文名称：前缀匹配（如 香港电信1 → hk）
        foreach (var (name, code) in _countryNames)
            if (normalized.StartsWith(name, StringComparison.Ordinal))
                return code;

        return null;
    }

    /// <summary>取备注中的第一个数字作为序号（如 HK-1 → 1）；没有数字返回 null。</summary>
    private static int? ExtractNumber(string remark)
    {
        var match = Regex.Match(remark, @"\d+");
        return match.Success && int.TryParse(match.Value, out var number) ? number : null;
    }

    /// <summary>把未知协议清理为只含小写字母/数字/短横线的 tag 片段。</summary>
    private static string SanitizeTag(string scheme)
    {
        var builder = new StringBuilder(scheme.Length);
        foreach (var c in scheme)
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        var tag = builder.ToString().Trim('-').ToLowerInvariant();
        return tag.Length == 0 ? "node" : tag;
    }

    /// <summary>清理备注中的控制字符与引号，避免破坏注释。</summary>
    private static string SanitizeComment(string remark)
    {
        var builder = new StringBuilder(remark.Length);
        foreach (var c in remark)
            if (c >= 0x20 && c != '"')
                builder.Append(c);
        return builder.ToString();
    }

    /// <summary>tag 冲突时追加 -2、-3… 保证唯一。</summary>
    private static string MakeUnique(string tag, HashSet<string> usedTags)
    {
        var candidate = tag;
        for (var i = 2; usedTags.Contains(candidate); i++)
            candidate = $"{tag}-{i}";
        usedTags.Add(candidate);
        return candidate;
    }

    /// <summary>预处理行：如果整个输入为单行 Base64 编码，则自动解码展开。</summary>
    private static IEnumerable<string> PreprocessLines(IEnumerable<string> rawLines)
    {
        var list = rawLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (list.Count == 1 && !list[0].Contains("://"))
        {
            try
            {
                var bytes = System.Convert.FromBase64String(list[0]);
                var decoded = Encoding.UTF8.GetString(bytes);
                return decoded.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                // 非 base64
            }
        }
        return list;
    }

    /// <summary>转换 URL 列表，返回 dae node 配置文本。</summary>
    public string ConvertLines(IEnumerable<string> lines)
    {
        var nodes = new List<(string Url, string Remark)>();
        var skipped = 0;

        lines = PreprocessLines(lines);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var hash = line.IndexOf('#');
            var url = (hash >= 0 ? line[..hash] : line).Trim();
            string remark;
            try
            {
                remark = hash >= 0 ? Uri.UnescapeDataString(line[(hash + 1)..].Trim()) : string.Empty;
            }
            catch
            {
                remark = hash >= 0 ? line[(hash + 1)..].Trim() : string.Empty;
            }

            if (url.IndexOf("://", StringComparison.Ordinal) <= 0)
            {
                skipped++;
                continue;
            }
            nodes.Add((url, remark));
        }

        if (skipped > 0)
            Log.Warn($"Skipped {skipped} unparsable lines");

        var usedTags = new HashSet<string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine("node {");
        builder.AppendLine("    # HTTPS/VMess/VLESS/Shadowsocks/Trojan/Tuic/Juicity/Hysteria2 等格式");

        foreach (var (url, remark) in nodes)
        {
            var scheme = url[..url.IndexOf("://", StringComparison.Ordinal)].ToLowerInvariant();
            var protocol = ProtocolAbbr.TryGetValue(scheme, out var abbr) ? abbr : SanitizeTag(scheme);

            var code = DetectCountry(remark) ?? "other";
            var group = $"{code}-{protocol}";
            counters.TryGetValue(group, out var count);

            // 序号优先取备注中的数字，取不到时按组递增；备注数字较大时递增起点跟着提高
            var number = ExtractNumber(remark) ?? ++count;
            if (number > count)
                count = number;
            counters[group] = count;

            var tag = MakeUnique($"{code}-{protocol}-{number:D2}", usedTags);

            var comment = remark.Length == 0 ? string.Empty : $" #{SanitizeComment(remark)}";
            builder.AppendLine($"    {tag}: \"{url}\"{comment}");
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>转换 URL 列表（JSON 数组形式），供 Web API 等 JSON 入口使用。</summary>
    public override object Convert(JsonNode data)
    {
        if (data is not JsonArray array)
            throw new InvalidOperationException("dae 转换器需要 URL 列表（JSON 数组），或使用 --input 指定 .txt 订阅文件");
        return ConvertLines(array.Select(x => x?.GetValue<string>() ?? string.Empty));
    }

    /// <summary>转换文件或 URL：逐行读取订阅 URL，输出到 {输入文件名去掉扩展名}_converted.dae。</summary>
    public override string ConvertFile(string inputPath, string? outputPath = null)
    {
        var content = ReadInputText(inputPath);
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var result = ConvertLines(lines);

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
            outPath = Path.Combine(Directory.GetCurrentDirectory(), $"{name}_converted.dae");
        }
        else
        {
            var fullInput = Path.GetFullPath(inputPath);
            outPath = Path.Combine(
                Path.GetDirectoryName(fullInput)!,
                $"{Path.GetFileNameWithoutExtension(fullInput)}_converted.dae");
        }

        File.WriteAllText(outPath, result, new UTF8Encoding(false));
        return outPath;
    }
}
