using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Sample;

/// <summary>Changes the bot's presence and builds some structure in the group the command was used in.</summary>
public sealed class StatusModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("status", "Changes the bot's status: online, dnd, invisible or offline")]
    public async Task ChangeStatus([SlashCommandOption("online, dnd, invisible or offline", Required = true)] string status)
    {
        UserStatus? parsed = status switch
        {
            "online" => UserStatus.Online,
            "dnd" => UserStatus.DoNotDisturb,
            "invisible" => UserStatus.Invisible,
            "offline" => UserStatus.Offline,
            _ => null,
        };
        if (parsed is not { } value)
        {
            await ReplyAsync("Unknown status - use online, dnd, invisible or offline.");
            return;
        }

        await client.SetStatusAsync(value);
        await ReplyAsync($"Status is now {value}.");
    }

    /// <summary>Creates a category, a text channel and a role in the command's group and gives the role to the caller.</summary>
    [SlashCommand("setup-demo", "Create a demo category, channel and role and assign the role to yourself")]
    [GroupOnly]
    [BotHasPermissions(Permissions.ManageChannels | Permissions.ManageRoles)]
    public async Task SetupDemo()
    {
        var groupId = Context.GroupId!.Value;
        await using var typing = Context.BeginTyping();
        var category = await client.CreateCategoryAsync(groupId, "Bot Demo");
        var channel = await client.CreateChannelAsync(category.Id, "bot-demo", ChannelType.Text);
        var role = await client.CreateRoleAsync(groupId, "Bot Demo Role", color: "#5865F2");
        await client.AssignRoleAsync(groupId, Context.Interaction.InvokingUserId, role.Id);

        await ReplyAsync($"Created category '{category.Name}', channel '{channel.Name}' and gave you the role '{role.Name}'.");
    }
}
