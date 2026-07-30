using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>One selectable option, shared by <see cref="SelectMenuBuilder"/> and <see cref="ComboBoxBuilder"/>.</summary>
public sealed record SelectOption(string Label, string Value);

/// <summary>
/// Fluent builder for a standard select-menu component (fixed dropdown). For a searchable/filterable
/// variant, use <see cref="ComboBoxBuilder"/> instead - see docs/message-components.md in the
/// Iopta.Backend repo for the schema.
/// </summary>
public class SelectMenuBuilder
{
    private readonly List<SelectOption> _options = [];
    private string? _customId;
    private string? _placeholder;
    private int _minValues = 1;
    private int _maxValues = 1;

    public SelectMenuBuilder WithCustomId(string customId)
    {
        _customId = customId;
        return this;
    }

    public SelectMenuBuilder WithPlaceholder(string placeholder)
    {
        _placeholder = placeholder;
        return this;
    }

    public SelectMenuBuilder AddOption(string label, string value)
    {
        _options.Add(new SelectOption(label, value));
        return this;
    }

    public SelectMenuBuilder WithValueRange(int minValues, int maxValues)
    {
        _minValues = minValues;
        _maxValues = maxValues;
        return this;
    }

    protected virtual string ComponentType => "select";

    internal virtual JsonObject ToJson()
    {
        if (string.IsNullOrEmpty(_customId))
            throw new InvalidOperationException("A select menu requires WithCustomId(...).");
        if (_options.Count == 0)
            throw new InvalidOperationException("A select menu requires at least one AddOption(...) call.");

        var options = new JsonArray();
        foreach (var option in _options)
            options.Add(new JsonObject { ["label"] = option.Label, ["value"] = option.Value });

        var json = new JsonObject
        {
            ["type"] = ComponentType,
            ["customId"] = _customId,
            ["options"] = options,
            ["minValues"] = _minValues,
            ["maxValues"] = _maxValues,
        };
        if (_placeholder is not null) json["placeholder"] = _placeholder;
        return json;
    }
}
