namespace Voice.Bot.Client.Net;

/// <summary>
/// Permission bits of a role, a channel overwrite, a member's effective permissions or a slash command's
/// default member permissions. The values are stable across API versions; combine them with <c>|</c>.
/// </summary>
/// <example>
/// <code>
/// var moderator = Permissions.KickMembers | Permissions.BanMembers | Permissions.ManageMessages;
/// var effective = await client.GetEffectivePermissionsAsync(groupId, userId);
/// if (effective.Has(Permissions.KickMembers)) { ... }
/// </code>
/// </example>
[Flags]
public enum Permissions : ulong
{
    /// <summary>No permissions.</summary>
    None = 0,

    /// <summary>See channels and read their messages.</summary>
    ViewChannels = 1UL << 0,

    /// <summary>Create, edit and delete categories and channels.</summary>
    ManageChannels = 1UL << 1,

    /// <summary>Create, edit, delete and assign roles below the member's highest role.</summary>
    ManageRoles = 1UL << 2,

    /// <summary>Manage the group's custom emoji and stickers.</summary>
    ManageEmojisAndStickers = 1UL << 3,

    /// <summary>Read the group's audit log.</summary>
    ViewAuditLog = 1UL << 4,

    /// <summary>See the group's insights.</summary>
    ViewServerInsights = 1UL << 5,

    /// <summary>Create, edit and delete webhooks.</summary>
    ManageWebhooks = 1UL << 6,

    /// <summary>Change the group's name, description and other settings.</summary>
    ManageServer = 1UL << 7,

    /// <summary>Create invites.</summary>
    CreateInvite = 1UL << 8,

    /// <summary>Change the member's own nickname.</summary>
    ChangeNickname = 1UL << 9,

    /// <summary>Change other members' nicknames.</summary>
    ManageNicknames = 1UL << 10,

    /// <summary>Remove members from the group.</summary>
    KickMembers = 1UL << 11,

    /// <summary>Ban and unban members.</summary>
    BanMembers = 1UL << 12,

    /// <summary>Create, edit and delete events.</summary>
    ManageEvents = 1UL << 13,

    /// <summary>Every permission; <see cref="PermissionsExtensions.Has"/> treats it as having all of them.</summary>
    Administrator = 1UL << 14,

    /// <summary>Send messages in text channels.</summary>
    SendMessages = 1UL << 15,

    /// <summary>Send messages in threads.</summary>
    SendMessagesInThreads = 1UL << 16,

    /// <summary>Start public threads.</summary>
    CreatePublicThreads = 1UL << 17,

    /// <summary>Start private threads.</summary>
    CreatePrivateThreads = 1UL << 18,

    /// <summary>Links in the member's messages show previews.</summary>
    EmbedLinks = 1UL << 19,

    /// <summary>Attach files to messages.</summary>
    AttachFiles = 1UL << 20,

    /// <summary>Add new reactions to messages.</summary>
    AddReactions = 1UL << 21,

    /// <summary>Use emoji from other groups.</summary>
    UseExternalEmoji = 1UL << 22,

    /// <summary>Use stickers from other groups.</summary>
    UseExternalStickers = 1UL << 23,

    /// <summary>Use <c>@everyone</c> and <c>@here</c>.</summary>
    MentionEveryone = 1UL << 24,

    /// <summary>Delete and pin other members' messages.</summary>
    ManageMessages = 1UL << 25,

    /// <summary>Rename, archive and delete threads.</summary>
    ManageThreads = 1UL << 26,

    /// <summary>Read messages sent before the member could see the channel.</summary>
    ReadMessageHistory = 1UL << 27,

    /// <summary>Send text-to-speech messages.</summary>
    SendTextToSpeechMessages = 1UL << 28,

    /// <summary>Use bots' slash commands.</summary>
    UseApplicationCommands = 1UL << 29,

    /// <summary>Join voice channels.</summary>
    Connect = 1UL << 30,

    /// <summary>Speak in voice channels.</summary>
    Speak = 1UL << 31,

    /// <summary>Share the camera or the screen.</summary>
    Video = 1UL << 32,

    /// <summary>Start activities in voice channels.</summary>
    StartActivities = 1UL << 33,

    /// <summary>Speak without push-to-talk.</summary>
    UseVoiceActivity = 1UL << 34,

    /// <summary>Be heard over other speakers.</summary>
    PrioritySpeaker = 1UL << 35,

    /// <summary>Mute other members in voice channels.</summary>
    MuteMembers = 1UL << 36,

    /// <summary>Deafen other members in voice channels.</summary>
    DeafenMembers = 1UL << 37,

    /// <summary>Move members between voice channels.</summary>
    MoveMembers = 1UL << 38,

    /// <summary>Ask to speak in tribune channels.</summary>
    RequestToSpeak = 1UL << 39,
}

/// <summary>Checks on <see cref="Permissions"/> that know about <see cref="Permissions.Administrator"/>.</summary>
public static class PermissionsExtensions
{
    /// <summary>
    /// Whether <paramref name="permissions"/> include every bit of <paramref name="required"/>.
    /// <see cref="Permissions.Administrator"/> includes everything. Unlike <see cref="Enum.HasFlag"/>,
    /// this is what the server checks.
    /// </summary>
    /// <example><c>effective.Has(Permissions.KickMembers | Permissions.BanMembers)</c></example>
    public static bool Has(this Permissions permissions, Permissions required) =>
        (permissions & Permissions.Administrator) != 0 || (permissions & required) == required;

    /// <summary>The bits of <paramref name="required"/> that <paramref name="permissions"/> lack; <see cref="Permissions.None"/> when <see cref="Has"/> is true.</summary>
    public static Permissions Missing(this Permissions permissions, Permissions required) =>
        (permissions & Permissions.Administrator) != 0 ? Permissions.None : required & ~permissions;
}
