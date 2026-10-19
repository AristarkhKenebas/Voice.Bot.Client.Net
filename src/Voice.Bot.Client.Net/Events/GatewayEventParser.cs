using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Voice.Bot.Client.Net.Gateway;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Net.Events;

/// <summary>A gateway message that couldn't be turned into an event; raised through <c>BotClient.ConnectionError</c>.</summary>
public sealed class GatewayParseException(string topic, string? type, Exception inner)
    : Exception($"Couldn't parse gateway event {topic}/{type ?? "?"}: {inner.Message}", inner)
{
    public string Topic { get; } = topic;
    public string? Type { get; } = type;
}

internal enum GatewayEventKind
{
    MessageCreated,
    MessageUpdated,
    MessageDeleted,
    ReactionAdded,
    ReactionRemoved,
    UserTyping,
    BotTyping,
    GroupStructureChanged,
    MemberJoined,
    MemberLeft,
    MemberKicked,
    UserProfileUpdated,
    UserStatusChanged,
    VoiceConnected,
    VoiceDisconnected,
    InteractionCreated,
    Unknown,
    ParseError,

    /// <summary>Not from the server: the gateway (re)connected; raises <c>BotClient.Ready</c>.</summary>
    Connected,

    /// <summary>Not from the server: completes its <see cref="TaskCompletionSource"/> once every earlier event is handled.</summary>
    Barrier,
}

/// <summary>One parsed gateway message: what kind of event it is and its payload record.</summary>
internal readonly record struct ParsedEvent(GatewayEventKind Kind, object Event);

/// <summary>
/// Pure <c>(topic, json) -&gt; event</c> parser for the two JSON layers of a gateway message (the
/// envelope, then <c>PayloadJson</c> by <c>Type</c>). Field names are matched case-insensitively,
/// enums accept both numbers and names, and missing fields fall back to defaults, so a backend change
/// in casing or a new optional field never drops events. Never throws - a failure comes back as
/// <see cref="GatewayParseException"/>.
/// </summary>
internal static partial class GatewayEventParser
{
    private static readonly HashSet<string> GroupStructureTypes =
    [
        "GroupUpdated", "CategoryCreated", "CategoryUpdated", "CategoryDeleted", "CategoryMoved", "ChannelCreated",
        "ChannelDeleted", "ChannelMoved", "RoleCreated", "RoleUpdated", "RoleMoved", "RoleDeleted", "RoleAssigned", "RoleRemoved",
    ];

    private static readonly Dictionary<string, InteractionType> InteractionTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ApplicationCommand"] = InteractionType.ApplicationCommand,
        ["MessageComponent"] = InteractionType.MessageComponent,
        ["ModalSubmit"] = InteractionType.ModalSubmit,
        ["ApplicationCommandAutocomplete"] = InteractionType.ApplicationCommandAutocomplete,
        ["Autocomplete"] = InteractionType.ApplicationCommandAutocomplete,
    };

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeZoneSuffix();

    public static ParsedEvent Parse(string topic, string json)
    {
        string? type = null;
        try
        {
            using var envelopeDocument = JsonDocument.Parse(json);
            var envelope = envelopeDocument.RootElement;
            type = Text(Field(envelope, "Type")) ?? "";
            var groupId = OptionalId(Field(envelope, "GroupId"));
            var payloadJson = Text(Field(envelope, "PayloadJson")) ?? "";

            using var payloadDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var payload = payloadDocument.RootElement.ValueKind == JsonValueKind.Object ? payloadDocument.RootElement : default;

            return topic switch
            {
                GatewayTopics.ChatEvents => Chat(type, groupId, envelope, payload),
                GatewayTopics.GroupEvents => Group(type, groupId, envelope, payload),
                GatewayTopics.ChannelEvents => Channel(type, groupId, envelope, payload),
                GatewayTopics.InteractionEvents => Interaction(type, groupId, payload),
                _ => null,
            } ?? new ParsedEvent(GatewayEventKind.Unknown, new UnknownGatewayEvent(topic, type, groupId, payloadJson));
        }
        catch (Exception ex)
        {
            return new ParsedEvent(GatewayEventKind.ParseError, new GatewayParseException(topic, type, ex));
        }
    }

    private static ParsedEvent? Chat(string type, Guid? groupId, JsonElement envelope, JsonElement payload) => type switch
    {
        "MessageCreated" => new(GatewayEventKind.MessageCreated, new MessageCreatedEvent(groupId, Message(payload))),
        "MessageUpdated" => new(GatewayEventKind.MessageUpdated, new MessageUpdatedEvent(groupId, Message(payload))),
        "MessageDeleted" => new(GatewayEventKind.MessageDeleted, new MessageDeletedEvent(
            groupId, OptionalId(Field(envelope, "ChatId")), OptionalId(Field(payload, "Id")) ?? Id(Field(envelope, "MessageId")))),
        "MessageReactionAdded" or "MessageReactionRemoved" => new(
            type == "MessageReactionAdded" ? GatewayEventKind.ReactionAdded : GatewayEventKind.ReactionRemoved,
            new ReactionEvent(
            groupId, Id(Field(payload, "ChannelId")), Id(Field(payload, "MessageId")), Id(Field(payload, "UserId")),
            Text(Field(payload, "Emoji")) ?? "", Reactions(Field(payload, "Reactions")))),
        "UserTyping" => new(GatewayEventKind.UserTyping, new TypingEvent(
            groupId, Id(Field(payload, "ChannelId")), Id(Field(payload, "UserId")), Text(Field(payload, "Username")) ?? "", Bool(payload, "IsTyping"))),
        "BotTyping" => new(GatewayEventKind.BotTyping, new BotTypingEvent(
            groupId, Id(Field(payload, "ChannelId")), Id(Field(payload, "BotId")), Text(Field(payload, "Username")) ?? "",
            Enum(Field(payload, "State"), BotTypingState.Unknown), Bool(payload, "IsActive"))),
        _ => null,
    };

    private static ParsedEvent? Group(string type, Guid? groupId, JsonElement envelope, JsonElement payload)
    {
        var id = groupId ?? Id(Field(payload, "Id"));
        var userId = OptionalId(Field(envelope, "UserId")) ?? Id(Field(payload, "UserId"));
        if (GroupStructureTypes.Contains(type))
        {
            return new(GatewayEventKind.GroupStructureChanged, new GroupStructureChangedEvent(type, id, Snapshot(payload)));
        }

        return type switch
        {
            "UserJoined" => new(GatewayEventKind.MemberJoined, new MemberEvent(id, userId, Snapshot(payload))),
            "UserLeft" => new(GatewayEventKind.MemberLeft, new MemberEvent(id, userId, Snapshot(payload))),
            "UserKicked" => new(GatewayEventKind.MemberKicked, new MemberEvent(id, userId, Snapshot(payload))),
            "UserProfileUpdated" => new(GatewayEventKind.UserProfileUpdated, new UserProfileUpdatedEvent(
                groupId, userId, Text(Field(payload, "DisplayName")), Text(Field(payload, "Description")), Text(Field(payload, "Status")),
                OptionalId(Field(payload, "AvatarId")), OptionalId(Field(payload, "BannerId")),
                OptionalEnum<UserStatus>(Field(payload, "PresenceStatus")), Time(Field(payload, "LastActivity")))),
            "UserStatusChanged" => new(GatewayEventKind.UserStatusChanged, new UserStatusChangedEvent(
                groupId, userId, OptionalEnum<UserStatus>(Field(payload, "PresenceStatus")), Text(Field(payload, "Status")),
                Time(Field(payload, "LastActivity")))),
            _ => null,
        };
    }

    private static ParsedEvent? Channel(string type, Guid? groupId, JsonElement envelope, JsonElement payload)
    {
        var channelId = Id(Field(envelope, "ChatId"));
        var user = GatewayUser(payload, OptionalId(Field(envelope, "UserId")));
        return type switch
        {
            "UserConnected" => new(GatewayEventKind.VoiceConnected, new VoiceEvent(groupId, channelId, user)),
            "UserDisconnected" => new(GatewayEventKind.VoiceDisconnected, new VoiceEvent(groupId, channelId, user)),
            _ => null,
        };
    }

    /// <param name="groupId">The envelope's group, used when the payload has none.</param>
    private static ParsedEvent? Interaction(string type, Guid? groupId, JsonElement payload)
    {
        if (type != "InteractionCreated") return null;
        var data = Field(payload, "Data");
        var focused = Field(data, "FocusedOption");
        if (focused.ValueKind == JsonValueKind.Object) focused = Field(focused, "Name");
        var values = Field(data, "Values");

        return new ParsedEvent(GatewayEventKind.InteractionCreated, new BotInteractionEvent(
            Id(Field(payload, "InteractionId")),
            Id(Field(payload, "InvokingUserId")),
            Id(Field(payload, "ChannelId")),
            OptionalId(Field(payload, "GroupId")) ?? groupId,
            InteractionKind(Field(payload, "Type")),
            new InteractionData(
                Text(Field(data, "CommandName")),
                StringMap(Field(data, "Options")),
                Text(Field(data, "CustomId")),
                values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(Text).OfType<string>().ToList() : [],
                Text(focused),
                StringMap(Field(data, "ModalFields")))));
    }

    private static Message Message(JsonElement payload)
    {
        var author = Field(payload, "Author");
        var interactionId = OptionalId(Field(payload, "InteractionId"));
        return new Message(
            Id(Field(payload, "Id")),
            Id(Field(payload, "ChannelId")),
            new User(
                Id(Field(author, "Id")), Text(Field(author, "Username")) ?? "", Text(Field(author, "DisplayName")), Text(Field(author, "Description")),
                Text(Field(author, "Status")), OptionalEnum<UserStatus>(Field(author, "PresenceStatus")) ?? UserStatus.Unknown,
                Text(Field(author, "AvatarUrl")), Text(Field(author, "BannerUrl")), Bool(author, "IsBot"), Time(Field(author, "CreatedAt"))),
            Text(Field(payload, "Content")) ?? "",
            Time(Field(payload, "CreatedAt")),
            Time(Field(payload, "UpdatedAt")),
            Bool(payload, "IsEdited"),
            Bool(payload, "IsDeleted"),
            OptionalId(Field(payload, "ReplyTo")),
            OptionalId(Field(payload, "ReplyToAuthorId")),
            OptionalId(Field(payload, "ForwardedFrom")),
            Reactions(Field(payload, "Reactions")),
            Objects(payload, "Attachments").Select(a => new Attachment(
                Id(Field(a, "Id")), OptionalId(Field(a, "UploaderId")), Text(Field(a, "FileName")) ?? "", Text(Field(a, "ContentType")) ?? "",
                Long(Field(a, "SizeBytes")), AttachmentStatusText.Parse(Text(Field(a, "Status"))), Text(Field(a, "DownloadUrl")))).ToList(),
            Objects(payload, "Mentions").Select(m => new MentionedUser(
                Id(Field(m, "Id")), Text(Field(m, "Username")) ?? "", Text(Field(m, "DisplayName")), Text(Field(m, "AvatarUrl")), Bool(m, "IsBot"))).ToList(),
            Objects(payload, "MentionChannels").Select(c => new MentionedChannel(
                Id(Field(c, "Id")), Text(Field(c, "Name")) ?? "", (ChannelType)Long(Field(c, "Type")), OptionalId(Field(c, "GroupId")))).ToList(),
            Objects(payload, "MentionRoles").Select(r => new MentionedRole(Id(Field(r, "Id")), Text(Field(r, "Name")) ?? "", (int)Long(Field(r, "Color")))).ToList(),
            Bool(payload, "MentionEveryone"),
            Bool(payload, "MentionHere"),
            Text(Field(payload, "ComponentsJson")),
            interactionId is { } interaction
                ? new MessageInteraction(interaction, Text(Field(payload, "InteractionCommandName")), OptionalId(Field(payload, "InteractionUserId")))
                : null);
    }

    private static GatewayUser GatewayUser(JsonElement data, Guid? fallbackId) => new(
        OptionalId(Field(data, "Id")) ?? fallbackId ?? Guid.Empty,
        Text(Field(data, "Username")) ?? "",
        Text(Field(data, "DisplayName")),
        Text(Field(data, "Description")),
        Text(Field(data, "Status")),
        OptionalId(Field(data, "AvatarId")),
        OptionalEnum<UserStatus>(Field(data, "PresenceStatus")),
        Bool(data, "IsBot"));

    private static GroupSnapshot? Snapshot(JsonElement payload)
    {
        if (OptionalId(Field(payload, "Id")) is not { } id) return null;
        return new GroupSnapshot(
            id,
            Id(Field(payload, "OwnerId")),
            Text(Field(payload, "Name")) ?? "",
            OptionalEnum<AccessType>(Field(payload, "AccessType")),
            OptionalEnum<VerificationLevel>(Field(payload, "VerificationLevel")),
            Time(Field(payload, "CreatedAt")),
            OptionalId(Field(payload, "AvatarId")),
            OptionalId(Field(payload, "BannerId")),
            Objects(payload, "Categories").Select(c => new CategorySnapshot(
                Id(Field(c, "Id")), Text(Field(c, "Name")) ?? "", Text(Field(c, "Position")) ?? "",
                Objects(c, "Channels").Select(ch => new ChannelSnapshot(
                    Id(Field(ch, "Id")), OptionalId(Field(ch, "CategoryId")), Text(Field(ch, "Name")) ?? "", (ChannelType)Long(Field(ch, "Type")),
                    Text(Field(ch, "Position")) ?? "", Objects(ch, "Users").Select(u => GatewayUser(u, null)).ToList())).ToList())).ToList());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Reactions(JsonElement value)
    {
        var result = new Dictionary<string, IReadOnlyList<Guid>>();
        if (value.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in value.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(OptionalId).OfType<Guid>().ToList()
                : [];
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement value)
    {
        var result = new Dictionary<string, string>();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (Text(property.Value) is { } text) result[property.Name] = text;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (Text(Field(item, "Name")) is { } name) result[name] = Text(Field(item, "Value")) ?? "";
            }
        }

        return result;
    }

    private static InteractionType InteractionKind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => InteractionTypeNames.GetValueOrDefault(value.GetString()!, InteractionType.Unknown),
        JsonValueKind.Number => Enum(value, InteractionType.Unknown),
        _ => InteractionType.Unknown,
    };

    private static T Enum<T>(JsonElement value, T unknown) where T : struct, Enum => OptionalEnum<T>(value) ?? unknown;

    private static T? OptionalEnum<T>(JsonElement value) where T : struct, Enum
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            var candidate = (T)(object)number;
            return System.Enum.IsDefined(candidate) ? candidate : null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString()!.Replace("_", "");
            return System.Enum.TryParse<T>(name, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed) ? parsed : null;
        }

        return null;
    }

    private static JsonElement Field(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        if (element.TryGetProperty(name, out var exact)) return exact;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return default;
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static bool Bool(JsonElement data, string name) => Field(data, name).ValueKind == JsonValueKind.True;

    private static long Long(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && Math.Abs(number) < long.MaxValue
            ? (long)number
            : 0;

    private static Guid? OptionalId(JsonElement value) => Guid.TryParse(Text(value), out var id) ? id : null;

    private static Guid Id(JsonElement value) => OptionalId(value) ?? Guid.Empty;

    private static DateTimeOffset? Time(JsonElement value)
    {
        var text = Text(value);
        if (string.IsNullOrEmpty(text)) return null;
        if (!TimeZoneSuffix().IsMatch(text)) text += "Z";
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time) ? time : null;
    }

    private static IEnumerable<JsonElement> Objects(JsonElement data, string name)
    {
        var value = Field(data, name);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object) : [];
    }
}
