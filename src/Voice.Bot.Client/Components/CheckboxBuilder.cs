using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>
/// Fluent builder for a standalone checkbox toggle - several in one row form a checklist. Checked
/// <c>customId</c>s report back the same way a select menu reports chosen values, see
/// docs/message-components.md in the Iopta.Backend repo.
/// </summary>
public sealed class CheckboxBuilder
{
    private string? _customId;
    private string? _label;
    private bool _checked;

    public CheckboxBuilder WithCustomId(string customId)
    {
        _customId = customId;
        return this;
    }

    public CheckboxBuilder WithLabel(string label)
    {
        _label = label;
        return this;
    }

    public CheckboxBuilder WithChecked(bool isChecked = true)
    {
        _checked = isChecked;
        return this;
    }

    internal JsonObject ToJson()
    {
        if (string.IsNullOrEmpty(_customId))
            throw new InvalidOperationException("A checkbox requires WithCustomId(...).");

        var json = new JsonObject { ["type"] = "checkbox", ["customId"] = _customId, ["checked"] = _checked };
        if (_label is not null) json["label"] = _label;
        return json;
    }
}
