using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Voice.Bot.Client.Net.Events;
using Xunit;

namespace Voice.Bot.Client.Net.Tests;

/// <summary>
/// Runs the cases shared by every Voice SDK (<c>testdata/conformance/gateway-cases.json</c>, see the README
/// there) through <see cref="GatewayEventParser"/> and compares the result in the canonical form.
/// </summary>
public class ConformanceTests
{
    private static readonly string CasesPath = Path.Combine(AppContext.BaseDirectory, "testdata", "conformance", "gateway-cases.json");

    /// <summary>Fields whose values are ids; the cases use short ids, this SDK <see cref="Guid"/>s.</summary>
    private static readonly HashSet<string> IdFields =
    [
        "id", "groupId", "channelId", "userId", "messageId", "botId", "ownerId", "categoryId", "uploaderId", "replyTo",
        "replyToAuthorId", "forwardedFrom", "avatarId", "bannerId",
    ];

    /// <summary>The few properties named differently from the canonical (Go SDK) names.</summary>
    private static readonly Dictionary<string, string> Names = new()
    {
        ["IsEdited"] = "edited",
        ["IsDeleted"] = "deleted",
        ["IsBot"] = "bot",
        ["IsTyping"] = "typing",
        ["IsActive"] = "active",
    };

    /// <summary>Enum values named differently from their lower snake_case form.</summary>
    private static readonly Dictionary<object, string> EnumNames = new()
    {
        [InteractionType.ApplicationCommandAutocomplete] = "autocomplete",
    };

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();
        foreach (var testCase in Cases()) names.Add(testCase["name"]!.GetValue<string>());
        return names;
    }

    [Fact]
    public void TheSharedFile_ShouldHaveCases() => Cases().Should().NotBeEmpty();

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void GatewayCase_ShouldParseLikeEverySdk(string name)
    {
        var testCase = Cases().Single(c => c["name"]!.GetValue<string>() == name);
        var expected = testCase["expected"]!;
        var ids = new IdMap();
        ids.Collect(expected["fields"]);

        var parsed = GatewayEventParser.Parse(testCase["topic"]!.GetValue<string>(), ids.Rewrite(testCase["envelope"]!.AsObject()));

        EventName(parsed).Should().Be(expected["event"]!.GetValue<string>(), testCase["description"]?.GetValue<string>());
        var actual = parsed.Event is BotInteractionEvent interaction ? Interaction(interaction, ids) : Canonical(parsed.Event, ids);
        Sorted(actual).Should().Be(Sorted(expected["fields"]), testCase["description"]?.GetValue<string>());
    }

    private static List<JsonNode> Cases() =>
        [.. JsonNode.Parse(File.ReadAllText(CasesPath))!["cases"]!.AsArray().Select(c => c!)];

    private static string EventName(ParsedEvent parsed) => parsed.Kind switch
    {
        GatewayEventKind.Unknown => "UnknownEvent",
        GatewayEventKind.ParseError => $"ParseError: {((Exception)parsed.Event).Message}",
        var kind => kind.ToString(),
    };

    /// <summary>Interactions nest their data in the canonical form: <c>{"interaction": {...}}</c>.</summary>
    private static JsonNode Interaction(BotInteractionEvent e, IdMap ids) => new JsonObject
    {
        ["interaction"] = new JsonObject
        {
            ["id"] = Canonical(e.InteractionId, ids),
            ["userId"] = Canonical(e.InvokingUserId, ids),
            ["channelId"] = Canonical(e.ChannelId, ids),
            ["groupId"] = Canonical(e.GroupId, ids),
            ["type"] = Canonical(e.Type, ids),
            ["commandName"] = Canonical(e.Data.CommandName, ids),
            ["options"] = Canonical(e.Data.Options, ids),
            ["customId"] = Canonical(e.Data.CustomId, ids),
            ["values"] = Canonical(e.Data.Values, ids),
            ["focusedOption"] = Canonical(e.Data.FocusedOption, ids),
            ["modalFields"] = Canonical(e.Data.ModalFields, ids),
        },
    };

    /// <summary>
    /// The canonical JSON of a parsed value: lowerCamelCase names (with <see cref="Names"/>), empty strings and
    /// <see cref="Guid.Empty"/> as <c>null</c>, UTC times with milliseconds, enums in lower snake_case and
    /// <c>unknown</c> for a missing or undefined one.
    /// </summary>
    private static JsonNode? Canonical(object? value, IdMap ids, Type? declared = null)
    {
        var enumType = declared is null ? null : Nullable.GetUnderlyingType(declared) ?? declared;
        if (value is null) return enumType is { IsEnum: true } ? JsonValue.Create("unknown") : null;
        switch (value)
        {
            case string text:
                return text.Length == 0 ? null : JsonValue.Create(text);
            case Guid id:
                return id == Guid.Empty ? null : JsonValue.Create(ids.Token(id));
            case DateTimeOffset time:
                return JsonValue.Create(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            case bool flag:
                return JsonValue.Create(flag);
            case int or long:
                return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case Enum:
                return JsonValue.Create(EnumName(value));
            case IDictionary map:
            {
                var result = new JsonObject();
                foreach (DictionaryEntry entry in map) result[(string)entry.Key] = Canonical(entry.Value, ids);
                return result;
            }

            case IEnumerable list:
            {
                var result = new JsonArray();
                foreach (var item in list) result.Add(Canonical(item, ids));
                return result;
            }

            default:
            {
                var result = new JsonObject();
                foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.Name == "EqualityContract") continue;
                    result[Names.GetValueOrDefault(property.Name) ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name)] =
                        Canonical(property.GetValue(value), ids, property.PropertyType);
                }

                return result;
            }
        }
    }

    private static string EnumName(object value)
    {
        if (EnumNames.TryGetValue(value, out var special)) return special;
        if (!Enum.IsDefined(value.GetType(), value)) return "unknown";
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);
    }

    /// <summary>Compact JSON with every object's keys in order, so two values compare by content.</summary>
    private static string Sorted(JsonNode? node) => Order(node)?.ToJsonString() ?? "null";

    private static JsonNode? Order(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Order(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Order)]),
        null => null,
        _ => JsonNode.Parse(node.ToJsonString()),
    };

    /// <summary>A fixed <see cref="Guid"/> for every short id the expected fields mention, and back.</summary>
    private sealed class IdMap
    {
        private readonly Dictionary<string, Guid> _guids = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, string> _tokens = [];

        public void Collect(JsonNode? node, string? field = null)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "reactions" && value is JsonObject reactions)
                        {
                            foreach (var (_, users) in reactions) Collect(users, "id");
                        }
                        else
                        {
                            Collect(value, key);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array) Collect(item, field);
                    break;
                case JsonValue value when field is not null && IdFields.Contains(field) && value.TryGetValue<string>(out var token):
                    Add(token);
                    break;
            }
        }

        public string Token(Guid id) => _tokens.TryGetValue(id, out var token) ? token : id.ToString();

        /// <summary>The envelope as the server would send it, with every known short id - also inside <c>PayloadJson</c> - replaced by its GUID.</summary>
        public string Rewrite(JsonObject envelope)
        {
            var copy = (JsonObject)Replace(envelope.DeepClone())!;
            foreach (var key in copy.Select(p => p.Key).ToList())
            {
                if (!string.Equals(key, "PayloadJson", StringComparison.OrdinalIgnoreCase) || copy[key] is not JsonValue payload ||
                    !payload.TryGetValue<string>(out var json)) continue;
                try
                {
                    var original = JsonNode.Parse(json)?.ToJsonString();
                    var replaced = Replace(JsonNode.Parse(json))?.ToJsonString();
                    if (replaced != original) copy[key] = replaced;
                }
                catch (JsonException)
                {
                }
            }

            return copy.ToJsonString();
        }

        /// <summary>Replaces the ids inside <paramref name="node"/> in place; returns it, or the new value when it is an id itself.</summary>
        private JsonNode? Replace(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        if (Swapped(obj[key]) is { } guid) obj[key] = guid;
                        else Replace(obj[key]);
                    }

                    return obj;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (Swapped(array[i]) is { } guid) array[i] = guid;
                        else Replace(array[i]);
                    }

                    return array;
                default:
                    return Swapped(node) ?? node;
            }
        }

        private JsonValue? Swapped(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) && _guids.TryGetValue(text, out var guid)
                ? JsonValue.Create(guid.ToString())
                : null;

        private void Add(string token)
        {
            if (_guids.ContainsKey(token)) return;
            var guid = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(token)));
            _guids[token] = guid;
            _tokens[guid] = token;
        }
    }
}
