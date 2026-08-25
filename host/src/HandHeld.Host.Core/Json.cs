using System.Text;
using System.Text.Json;

namespace HandHeld.Host.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static byte[] Bytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? Parse<T>(byte[] utf8) => JsonSerializer.Deserialize<T>(utf8, Options);

    public static T? Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string String(object value) => JsonSerializer.Serialize(value, Options);
}
