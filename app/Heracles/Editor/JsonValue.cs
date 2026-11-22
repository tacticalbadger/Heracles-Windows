using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Heracles.Editor;

/// <summary>
/// A JSON value. Documents come from the core as JSON text, and the editor
/// keeps every attribute it does not understand in this form, so that a
/// document written by a newer editor survives being edited by this one.
/// Values never change, and two that hold the same things are equal.
/// </summary>
internal abstract partial record JsonValue
{
    public static JsonValue Null { get; } = new JsonNull();

    public static JsonValue EmptyArray { get; } = new JsonArray([]);

    public static JsonValue EmptyObject { get; } = new JsonObject(ImmutableSortedDictionary.Create<string, JsonValue>(StringComparer.Ordinal));

    public static JsonValue Of(bool value) => new JsonBool(value);

    public static JsonValue Of(double value) => new JsonNumber(value);

    public static JsonValue Of(string value) => new JsonString(value);

    public static JsonValue ArrayOf(IEnumerable<JsonValue> items) => new JsonArray([.. items]);

    public static JsonValue ObjectOf(IEnumerable<KeyValuePair<string, JsonValue>> members) =>
        new JsonObject(members.ToImmutableSortedDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal));

    public static JsonValue ObjectOf(params (string Key, JsonValue Value)[] members) =>
        ObjectOf(members.Select(member => new KeyValuePair<string, JsonValue>(member.Key, member.Value)));

    /// <summary>Parse JSON text. Null when the text is not JSON.</summary>
    public static JsonValue? Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return From(document.RootElement);
        }
        catch (JsonException) { return null; }
    }

    private static JsonValue From(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => Of(true),
        JsonValueKind.False => Of(false),
        JsonValueKind.Number => Of(element.GetDouble()),
        JsonValueKind.String => Of(element.GetString() ?? ""),
        JsonValueKind.Array => ArrayOf(element.EnumerateArray().Select(From)),
        // A name given twice keeps its last value, as other readers of JSON do.
        JsonValueKind.Object => new JsonObject(ImmutableSortedDictionary.CreateRange(
            StringComparer.Ordinal,
            element.EnumerateObject().GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => new KeyValuePair<string, JsonValue>(group.Key, From(group.Last().Value))))),
        _ => Null,
    };

    private static readonly JsonWriterOptions Writing = new()
    {
        // Slashes and letters outside ASCII are written as they are.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The value as JSON text, with keys in alphabetical order so that the
    /// same value always gives the same text.
    /// </summary>
    public string Text
    {
        get
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, Writing)) Write(writer);
            return Unescaped().Replace(Encoding.UTF8.GetString(stream.ToArray()), match =>
                match.Groups[1].Success
                    ? $"{(char)Convert.ToInt32(match.Groups[1].Value, 16)}{(char)Convert.ToInt32(match.Groups[2].Value, 16)}"
                    : match.Value);
        }
    }

    /// <summary>
    /// The writer spells every character outside the basic plane (an emoji)
    /// as two escapes, whatever it is told. They are put back as the
    /// characters, as the Mac and the core write them. An escaped backslash
    /// is matched first and left alone, so text that merely looks like an
    /// escape is not touched.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\\\\|\\u(D[89AB][0-9A-F]{2})\\u(D[C-F][0-9A-F]{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Unescaped();

    private void Write(Utf8JsonWriter writer)
    {
        switch (this)
        {
            case JsonBool boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case JsonNumber number:
                // Whole numbers are written without a fraction: 1, not 1.0.
                if (Math.Round(number.Value) == number.Value && Math.Abs(number.Value) < 9_007_199_254_740_992) writer.WriteNumberValue((long)number.Value);
                else writer.WriteNumberValue(number.Value);
                break;
            case JsonString text:
                writer.WriteStringValue(text.Value);
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array.Items) item.Write(writer);
                writer.WriteEndArray();
                break;
            case JsonObject members:
                writer.WriteStartObject();
                foreach (var (key, value) in members.Members)
                {
                    writer.WritePropertyName(key);
                    value.Write(writer);
                }
                writer.WriteEndObject();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    public JsonValue? this[string key] => this is JsonObject members ? members.Members.GetValueOrDefault(key) : null;

    public string? StringValue => (this as JsonString)?.Value;

    public bool? BoolValue => (this as JsonBool)?.Value;

    public double? DoubleValue => (this as JsonNumber)?.Value;

    /// <summary>The number, if it is a whole one that fits.</summary>
    public int? IntValue =>
        this is JsonNumber number && Math.Round(number.Value) == number.Value && Math.Abs(number.Value) <= int.MaxValue
            ? (int)number.Value
            : null;

    public IReadOnlyList<JsonValue>? ArrayValue => (this as JsonArray)?.Items;

    public IReadOnlyDictionary<string, JsonValue>? ObjectValue => (this as JsonObject)?.Members;
}

internal sealed record JsonNull : JsonValue;

internal sealed record JsonBool(bool Value) : JsonValue;

internal sealed record JsonNumber(double Value) : JsonValue;

internal sealed record JsonString(string Value) : JsonValue;

internal sealed record JsonArray(ImmutableArray<JsonValue> Items) : JsonValue
{
    public bool Equals(JsonArray? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => Items.Aggregate(17, (hash, item) => HashCode.Combine(hash, item));
}

internal sealed record JsonObject(ImmutableSortedDictionary<string, JsonValue> Members) : JsonValue
{
    public bool Equals(JsonObject? other) =>
        other is not null && Members.Count == other.Members.Count
        && Members.All(member => other.Members.TryGetValue(member.Key, out var value) && value == member.Value);

    public override int GetHashCode() => Members.Aggregate(19, (hash, member) => HashCode.Combine(hash, member.Key, member.Value));
}
