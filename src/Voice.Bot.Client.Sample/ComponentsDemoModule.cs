using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

namespace Voice.Bot.Client.Sample;

/// <summary>
/// Demonstrates the typed component/modal builders (<see cref="ComponentBuilder"/>/<see cref="ModalBuilder"/>)
/// and the bot role/structure management methods on <see cref="Net.BotClient"/> - no hand-written JSON
/// anywhere. See docs/message-components.md in the Iopta.Backend repo for the schema these builders
/// serialize to.
/// </summary>
public sealed class ComponentsDemoModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("survey", "Send a message with a button and a combobox")]
    public Task Survey()
    {
        var components = new ComponentBuilder()
            .WithButton(b => b.WithStyle(ButtonStyle.Primary).WithCustomId("survey:open").WithLabel("Open form"))
            .WithComboBox(c => c
                .WithCustomId("survey:pick")
                .WithPlaceholder("Pick a topic")
                .AddOption("Backend", "backend")
                .AddOption("Frontend", "frontend")
                .AddOption("Voice", "voice"));

        return RespondAsync("Click the button to open the form, or pick a topic below.", components: components);
    }

    [ComponentInteraction("survey:open")]
    public Task OpenSurveyModal()
    {
        var modal = new ModalBuilder()
            .WithTitle("Quick feedback")
            .WithCustomId("survey-modal")
            .AddTextInput(t => t.WithCustomId("comment").WithLabel("Comment").WithStyle(TextInputStyle.Paragraph))
            .AddCheckbox(c => c.WithCustomId("subscribe").WithLabel("Notify me of replies").WithChecked());

        return RespondWithModalAsync(modal);
    }

    [ModalInteraction("survey-modal")]
    public Task HandleSurveySubmit(string comment, string subscribe) =>
        RespondAsync($"Thanks! Comment: \"{comment}\", subscribed: {subscribe}", ephemeral: true);

    /// <summary>
    /// Creates a category + text channel in the interaction's group, then a "Muted" role and assigns it
    /// to the invoking user - exercises every new BotsApi role/structure method in one command.
    /// </summary>
    [SlashCommand("setup-demo", "Create a demo category/channel/role and assign the role to yourself")]
    public async Task SetupDemo()
    {
        if (Context.Interaction.GroupId is not { } groupId)
        {
            await RespondAsync("This command only works inside a group.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var category = await client.CreateCategoryAsync(groupId, "Bot Demo");
        var channel = await client.CreateChannelAsync(category.Id, "bot-demo", ChannelType.Text);
        var role = await client.CreateRoleAsync(groupId, "Bot Demo Role", color: "#5865F2");
        await client.AssignRoleAsync(groupId, Context.Interaction.InvokingUserId, role.Id);

        await FollowupAsync($"Created category '{category.Name}', channel '{channel.Name}', and assigned role '{role.Name}' to you.");
    }
}
