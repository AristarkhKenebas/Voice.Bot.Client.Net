using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Net.Components;

/// <summary>Mirrors the <c>textInput</c> modal component kind in docs/message-components.md (Iopta.Backend repo).</summary>
public enum TextInputStyle
{
    Short,
    Paragraph,
}

/// <summary>Fluent builder for a modal text-input field - only valid inside <see cref="ModalBuilder"/>.</summary>
public sealed class TextInputBuilder
{
    private string? _customId;
    private string? _label;
    private TextInputStyle _style = TextInputStyle.Short;
    private bool _required;
    private string? _placeholder;
    private int? _maxLength;

    public TextInputBuilder WithCustomId(string customId)
    {
        _customId = customId;
        return this;
    }

    public TextInputBuilder WithLabel(string label)
    {
        _label = label;
        return this;
    }

    public TextInputBuilder WithStyle(TextInputStyle style)
    {
        _style = style;
        return this;
    }

    public TextInputBuilder WithRequired(bool required = true)
    {
        _required = required;
        return this;
    }

    public TextInputBuilder WithPlaceholder(string placeholder)
    {
        _placeholder = placeholder;
        return this;
    }

    public TextInputBuilder WithMaxLength(int maxLength)
    {
        _maxLength = maxLength;
        return this;
    }

    internal JsonObject ToJson()
    {
        if (string.IsNullOrEmpty(_customId))
            throw new InvalidOperationException("A text input requires WithCustomId(...).");

        var json = new JsonObject
        {
            ["type"] = "textInput",
            ["customId"] = _customId,
            ["style"] = _style.ToString().ToLowerInvariant(),
            ["required"] = _required,
        };
        if (_label is not null) json["label"] = _label;
        if (_placeholder is not null) json["placeholder"] = _placeholder;
        if (_maxLength.HasValue) json["maxLength"] = _maxLength.Value;
        return json;
    }
}
