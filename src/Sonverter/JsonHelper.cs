namespace Sonverter;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>JSON 序列化辅助：与 Python <c>json.dumps(ensure_ascii=False, indent=2)</c> 对齐。</summary>
public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 与 Python json.dumps(ensure_ascii=False) 一致：中文与 emoji 原样输出
        Encoder = PassThroughEncoder.Instance,
    };

    public static string Serialize(JsonNode node) => node.ToJsonString(Options);
}

/// <summary>
/// 仅转义 JSON 语法必需字符（引号、反斜杠、控制字符）的编码器，
/// 其余字符（含中文、emoji）原样输出。
/// </summary>
internal sealed class PassThroughEncoder : JavaScriptEncoder
{
    public static readonly PassThroughEncoder Instance = new();

    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalarValue)
        => unicodeScalarValue is '"' or '\\' || unicodeScalarValue < 0x20;

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (var i = 0; i < textLength; i++)
        {
            var c = text[i];
            if (c is '"' or '\\' || c < 0x20)
                return i;
        }
        return -1;
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalarValue, char* buffer, int bufferLength, out int written)
    {
        written = 0;
        switch (unicodeScalarValue)
        {
            case '"':
                return TryWrite(buffer, bufferLength, "\\\"", ref written);
            case '\\':
                return TryWrite(buffer, bufferLength, "\\\\", ref written);
            case '\n':
                return TryWrite(buffer, bufferLength, "\\n", ref written);
            case '\r':
                return TryWrite(buffer, bufferLength, "\\r", ref written);
            case '\t':
                return TryWrite(buffer, bufferLength, "\\t", ref written);
            case '\b':
                return TryWrite(buffer, bufferLength, "\\b", ref written);
            case '\f':
                return TryWrite(buffer, bufferLength, "\\f", ref written);
            default:
                if (unicodeScalarValue < 0x20)
                {
                    if (bufferLength < 6)
                        return false;
                    buffer[0] = '\\';
                    buffer[1] = 'u';
                    buffer[2] = Hex(unicodeScalarValue >> 12);
                    buffer[3] = Hex((unicodeScalarValue >> 8) & 0xF);
                    buffer[4] = Hex((unicodeScalarValue >> 4) & 0xF);
                    buffer[5] = Hex(unicodeScalarValue & 0xF);
                    written = 6;
                    return true;
                }
                return false;
        }
    }

    private static unsafe bool TryWrite(char* buffer, int bufferLength, string value, ref int written)
    {
        if (bufferLength < value.Length)
            return false;
        for (var i = 0; i < value.Length; i++)
            buffer[i] = value[i];
        written = value.Length;
        return true;
    }

    private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}
