using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Moderation slash commands guarded by permissions instead of "owner only": the member needs the
/// permission in the channel, and so does the bot - otherwise the check answers with what's missing.
/// </summary>
[GroupOnly]
public sealed class ModerationModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("kick", "Removes a member from the group")]
    [HasPermissions(Permissions.KickMembers)]
    [BotHasPermissions(Permissions.KickMembers)]
    public async Task Kick([SlashCommandOption("Who to kick", Required = true, Type = CommandOptionType.User)] Guid user)
    {
        await client.KickUserAsync(Context.GroupId!.Value, user);
        await ReplyAsync($"{Mention.User(user)} was kicked.");
    }

    [SlashCommand("ban", "Bans a member")]
    [HasPermissions(Permissions.BanMembers)]
    [BotHasPermissions(Permissions.BanMembers)]
    public async Task Ban(
        [SlashCommandOption("Who to ban", Required = true, Type = CommandOptionType.User)] Guid user,
        [SlashCommandOption("Why")] string reason = "")
    {
        await client.BanUserAsync(Context.GroupId!.Value, user, reason);
        await ReplyAsync($"{Mention.User(user)} was banned.");
    }

    [SlashCommand("purge", "Deletes the latest messages in this channel", RunMode = RunMode.Async)]
    [HasPermissions(Permissions.ManageMessages)]
    [BotHasPermissions(Permissions.ManageMessages | Permissions.ReadMessageHistory)]
    [Cooldown(1, 30, CooldownScope.Channel)]
    public async Task Purge([SlashCommandOption("How many (1-100)")] int count = 10)
    {
        count = Math.Clamp(count, 1, 100);
        if (!await Context.ConfirmAsync($"Delete the last {count} messages?")) return;

        var deleted = 0;
        await using (Context.BeginTyping())
        {
            await foreach (var message in client.EnumerateMessagesAsync(Context.ChannelId, limit: count + 1))
            {
                if (message.Id == Context.LastSent?.Id) continue;
                await client.DeleteMessageAsync(message.Id);
                deleted++;
            }
        }

        await Context.SendAsync($"Deleted {deleted} messages.");
    }
}
