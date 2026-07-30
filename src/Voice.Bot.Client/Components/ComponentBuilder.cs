using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>
/// Fluent builder for a message's <c>components_json</c> (an <c>ActionRow[]</c> - see
/// docs/message-components.md in the Iopta.Backend repo). Pass to <see cref="BotClient.SendMessageAsync"/>,
/// <see cref="BotClient.UpdateMessageAsync"/>, <see cref="BotClient.SendInteractionFollowupAsync"/>, or
/// <see cref="Interactions.InteractionContext.RespondAsync"/>/<see cref="Interactions.InteractionContext.UpdateAsync"/>
/// instead of hand-writing JSON. Each <c>With*</c> method adds its own single-component row; use
/// <see cref="AddRow"/> when multiple components (e.g. several buttons) must share one row.
/// </summary>
public sealed class ComponentBuilder
{
    private readonly List<ActionRowBuilder> _rows = [];

    public ComponentBuilder WithButton(Action<ButtonBuilder> configure) =>
        AddRow(row => row.AddButton(configure));

    public ComponentBuilder WithSelectMenu(Action<SelectMenuBuilder> configure) =>
        AddRow(row => row.AddSelectMenu(configure));

    public ComponentBuilder WithComboBox(Action<ComboBoxBuilder> configure) =>
        AddRow(row => row.AddComboBox(configure));

    public ComponentBuilder WithCheckbox(Action<CheckboxBuilder> configure) =>
        AddRow(row => row.AddCheckbox(configure));

    public ComponentBuilder AddRow(Action<ActionRowBuilder> configure)
    {
        var row = new ActionRowBuilder();
        configure(row);
        _rows.Add(row);
        return this;
    }

    /// <summary>Serializes to the <c>components_json</c> string the backend expects.</summary>
    public string Build()
    {
        var array = new JsonArray();
        foreach (var row in _rows)
            array.Add(row.ToJson());

        return array.ToJsonString();
    }
}
