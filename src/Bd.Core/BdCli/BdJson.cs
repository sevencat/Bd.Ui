using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bd.Core.BdCli;

/// <summary>bd 的 JSON 信封：{ "data": ..., "schema_version": 1 }。</summary>
public sealed class BdEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }
}

/// <summary>错误信封的 data 部分：{ "error": "...", "hint": "..." }。</summary>
public sealed class BdErrorPayload
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("hint")]
    public string? Hint { get; init; }
}

public static class BdJson
{
    public const int SupportedSchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>解析信封，data 既可能是数组也可能是单个对象。</summary>
    public static IReadOnlyList<T> ParseArray<T>(string stdout, out int schemaVersion)
    {
        var root = ParseRoot(stdout);
        schemaVersion = ReadSchemaVersion(root);
        var data = DataOf(root);
        return data.ValueKind switch
        {
            JsonValueKind.Array => data.Deserialize<List<T>>(Options) ?? [],
            JsonValueKind.Object => Single<T>(data),
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            _ => throw Malformed($"data 既不是数组也不是对象（{data.ValueKind}）")
        };
    }

    public static T? ParseSingleOrDefault<T>(string stdout, out int schemaVersion)
    {
        var items = ParseArray<T>(stdout, out schemaVersion);
        return items.Count == 0 ? default : items[0];
    }

    public static int ReadSchemaVersion(string stdout) => ReadSchemaVersion(ParseRoot(stdout));

    public static bool TryReadError(string stdout, out BdErrorPayload? payload)
    {
        payload = null;
        try
        {
            var root = ParseRoot(stdout);
            var data = DataOf(root);
            if (data.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var candidate = data.Deserialize<BdErrorPayload>(Options);
            if (string.IsNullOrWhiteSpace(candidate?.Error))
            {
                return false;
            }

            payload = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonElement ParseRoot(string stdout)
    {
        var text = stdout.AsSpan().Trim();
        if (text.IsEmpty)
        {
            throw Malformed("stdout 为空，无法解析 JSON");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw Malformed($"JSON 解析失败：{ex.Message}");
        }
    }

    private static JsonElement DataOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data : root;

    private static int ReadSchemaVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("schema_version", out var version)
        && version.TryGetInt32(out var value)
            ? value
            : 0;

    private static IReadOnlyList<T> Single<T>(JsonElement data)
    {
        var item = data.Deserialize<T>(Options);
        return item is null ? [] : [item];
    }

    private static BdException Malformed(string message) =>
        new(BdErrorCode.Unknown, $"bd 返回的数据无法解析：{message}");
}
