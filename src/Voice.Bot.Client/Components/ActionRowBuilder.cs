using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>
/// One row of components (buttons/select/combobox/checkbox, or text inputs inside a modal). Most callers
/// don't need this directly - <see cref="ComponentBuilder"/>'s <c>With*</c> methods each add a
/// single-component row; use <see cref="ComponentBuilder.AddRow"/> when several components must share a row.
/// </summary>
public sealed class ActionRowBuilder
{
    private readonly List<Func<JsonObject>> _elements = [];

    public ActionRowBuilder AddButton(Action<ButtonBuilder> configure)
    {
        var builder = new ButtonBuilder();
        configure(builder);
        _elements.Add(builder.ToJson);
        return this;
    }

    public ActionRowBuilder AddSelectMenu(Action<SelectMenuBuilder> configure)
    {
        var builder = new SelectMenuBuilder();
        configure(builder);
        _elements.Add(builder.ToJson);
        return this;
    }

    public ActionRowBuilder AddComboBox(Action<ComboBoxBuilder> configure)
    {
        var builder = new ComboBoxBuilder();
        configure(builder);
        _elements.Add(builder.ToJson);
        return this;
    }

    public ActionRowBuilder AddCheckbox(Action<CheckboxBuilder> configure)
    {
        var builder = new CheckboxBuilder();
        configure(builder);
        _elements.Add(builder.ToJson);
        return this;
    }

    public ActionRowBuilder AddTextInput(Action<TextInputBuilder> configure)
    {
        var builder = new TextInputBuilder();
        configure(builder);
        _elements.Add(builder.ToJson);
        return this;
    }

    internal JsonObject ToJson()
    {
        var components = new JsonArray();
        foreach (var element in _elements)
            components.Add(element());

        return new JsonObject { ["type"] = "actionRow", ["components"] = components };
    }
}
