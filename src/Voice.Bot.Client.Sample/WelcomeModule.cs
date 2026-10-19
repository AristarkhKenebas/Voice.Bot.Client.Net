using Voice.Bot.Client.Net;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// A feature without commands: greets new members in <c>#welcome</c> (or <c>#general</c>) and posts a
/// reminder every morning. Registered with <c>AddBotModule&lt;WelcomeModule&gt;()</c>.
/// </summary>
public sealed class WelcomeModule : IBotModule
{
    public void Install(BotClient client)
    {
        client.OnMemberJoin(async joined =>
        {
            var channel = await client.Cache.GetChannelByNameAsync(joined.GroupId, "welcome")
                ?? await client.Cache.GetChannelByNameAsync(joined.GroupId, "general");
            if (channel is not null)
            {
                await client.SendMessageAsync(channel.Id, $"Welcome, {Mention.User(joined.UserId)}! Type `!help` to see what I can do.");
            }
        });

        client.At(new TimeOnly(9, 0), async ct =>
        {
            foreach (var group in await client.Cache.GetGroupsAsync(ct))
            {
                if (await client.Cache.GetChannelByNameAsync(group.Id, "general", ct) is { } general)
                {
                    await client.SendMessageAsync(general.Id, "Good morning!", ct: ct);
                }
            }
        });
    }
}
