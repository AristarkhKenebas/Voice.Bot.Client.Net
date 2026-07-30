using Voice.Bot.Client.Interactions;
using Voice.Bot.Client.Models;

namespace Voice.Bot.Client.Sample;

public class StatusModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("status", "Replies with the bot's status")]
    public async Task ChangeStatus([SlashCommandOption("Status", Required = true)] string status)
    {
        switch (status)
        {
            case "dnd":
                await client.SetStatusAsync(UserStatus.DoNotDisturb, "I'm busy");
                await RespondAsync("Do not disturb");
                break;
            case "online":
                await client.SetStatusAsync(UserStatus.Online, "Playing with the API");
                await RespondAsync("Online");
                break;
            case "offline":
                await client.SetStatusAsync(UserStatus.Offline);
                await RespondAsync("Offline");
                break;
            case "online-test":
                await client.SetStatusAsync(UserStatus.Online);
                await RespondAsync("OnlineTest");
                break;
            default:
                await RespondAsync("Invalid status");
                break;
        }
    }

    [SlashCommand("question", "Ask a question Opencode")]
    public async Task Question([SlashCommandOption("Question", Required = true)] string question)
    {
        await client.SetTypingAsync(BotTypingState.Thinking, Context.Interaction.ChannelId);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await client.SetTypingAsync(BotTypingState.Explored, Context.Interaction.ChannelId);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await RespondAsync("Opencode: " + question + "aadsasdlkjasdlkjasdlkjasdkljaspodjaspjodas[kod");
    }
}