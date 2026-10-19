namespace Voice.Bot.Client.Net;

/// <summary>
/// A reusable feature: the event handlers and scheduled tasks that belong together, installed with
/// <see cref="BotClient.Install"/> or, with dependency injection, <c>services.AddBotModule&lt;T&gt;()</c>.
/// Its commands live next to it as <see cref="Commands.ModuleBase"/> / <see cref="Interactions.InteractionModuleBase"/>
/// classes, with their checks as attributes.
/// </summary>
/// <example>
/// <code>
/// public sealed class WelcomeModule(ILogger&lt;WelcomeModule&gt; logger) : IBotModule
/// {
///     public void Install(BotClient client) =&gt;
///         client.OnMemberJoin(async e =&gt;
///         {
///             var channel = await client.Cache.GetChannelByNameAsync(e.GroupId, "welcome");
///             if (channel is not null) await client.SendMessageAsync(channel.Id, $"Welcome, {Mention.User(e.UserId)}!");
///         });
/// }
/// </code>
/// </example>
public interface IBotModule
{
    /// <summary>Subscribes the module's handlers and schedules its tasks.</summary>
    void Install(BotClient client);
}

public sealed partial class BotClient
{
    /// <summary>Installs a module: lets it subscribe its handlers and schedule its tasks on this client.</summary>
    public BotClient Install(IBotModule module)
    {
        module.Install(this);
        return this;
    }
}
