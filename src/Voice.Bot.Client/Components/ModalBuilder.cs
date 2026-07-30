using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>
/// Fluent builder for a <c>modal_json</c> payload (see docs/message-components.md in the Iopta.Backend
/// repo). Pass to <see cref="Interactions.InteractionContext.RespondWithModalAsync(ModalBuilder)"/>
/// instead of hand-writing JSON. Each <c>Add*</c> method adds its own single-component row; use
/// <see cref="AddRow"/> for a row with multiple components.
/// </summary>
public sealed class ModalBuilder
{
    private readonly List<ActionRowBuilder> _rows = [];
    private string? _title;
    private string? _customId;

    public ModalBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public ModalBuilder WithCustomId(string customId)
    {
        _customId = customId;
        return this;
    }

    public ModalBuilder AddTextInput(Action<TextInputBuilder> configure) =>
        AddRow(row => row.AddTextInput(configure));

    public ModalBuilder AddCheckbox(Action<CheckboxBuilder> configure) =>
        AddRow(row => row.AddCheckbox(configure));

    public ModalBuilder AddRow(Action<ActionRowBuilder> configure)
    {
        var row = new ActionRowBuilder();
        configure(row);
        _rows.Add(row);
        return this;
    }

    /// <summary>Serializes to the <c>modal_json</c> string the backend expects.</summary>
    public string Build()
    {
        if (string.IsNullOrEmpty(_title))
            throw new InvalidOperationException("A modal requires WithTitle(...).");
        if (string.IsNullOrEmpty(_customId))
            throw new InvalidOperationException("A modal requires WithCustomId(...).");

        var components = new JsonArray();
        foreach (var row in _rows)
            components.Add(row.ToJson());

        var json = new JsonObject { ["title"] = _title, ["customId"] = _customId, ["components"] = components };
        return json.ToJsonString();
    }
}
