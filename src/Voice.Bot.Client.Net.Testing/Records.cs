namespace Voice.Bot.Client.Net.Testing;

/// <summary>A reaction the bot added or removed.</summary>
public sealed record SentReaction(Guid MessageId, string Emoji);

/// <summary>A change of the bot's typing indicator.</summary>
public sealed record TypingSignal(Guid ChannelId, bool IsTyping);

/// <summary>A message the bot edited, with its new text.</summary>
public sealed record EditedMessage(Guid MessageId, string Content);

/// <summary>A file the bot uploaded.</summary>
public sealed record UploadedFile(Guid AttachmentId, string FileName, string ContentType, byte[] Data);

/// <summary>A kick, ban or unban the bot did; <see cref="Action"/> is <c>"kick"</c>, <c>"ban"</c> or <c>"unban"</c>.</summary>
public sealed record ModerationAction(string Action, Guid GroupId, Guid UserId, string? Reason);
