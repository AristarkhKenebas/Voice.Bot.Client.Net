using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Components;

/// <summary>Mirrors the <c>button</c> component kind in docs/message-components.md (Iopta.Backend repo).</summary>
public enum ButtonStyle
{
    Primary,
    Secondary,
    Success,
    Danger,
    Link,
}

/// <summary>
/// Fluent builder for a single button component. Add via <see cref="ComponentBuilder.WithButton"/> or
/// <see cref="ActionRowBuilder.AddButton"/> - never construct the underlying JSON by hand, see
/// docs/message-components.md in the Iopta.Backend repo for the schema this serializes to.
/// </summary>
public sealed class ButtonBuilder
{
    private ButtonStyle _style = ButtonStyle.Primary;
    private string? _customId;
    private string? _url;
    private string? _label;
    private string? _emoji;
    private bool _disabled;

    public ButtonBuilder WithStyle(ButtonStyle style)
    {
        _style = style;
        return this;
    }

    public ButtonBuilder WithCustomId(string customId)
    {
        _customId = customId;
        return this;
    }

    /// <summary>Only meaningful with <see cref="ButtonStyle.Link"/> - a link button has no <c>customId</c> and never triggers an interaction.</summary>
    public ButtonBuilder WithUrl(string url)
    {
        _url = url;
        return this;
    }

    public ButtonBuilder WithLabel(string label)
    {
        _label = label;
        return this;
    }

    public ButtonBuilder WithEmoji(string emoji)
    {
        _emoji = emoji;
        return this;
    }

    public ButtonBuilder WithDisabled(bool disabled = true)
    {
        _disabled = disabled;
        return this;
    }

    internal JsonObject ToJson()
    {
        if (_style == ButtonStyle.Link && string.IsNullOrEmpty(_url))
            throw new InvalidOperationException("A link-style button requires WithUrl(...).");
        if (_style != ButtonStyle.Link && string.IsNullOrEmpty(_customId))
            throw new InvalidOperationException("A button requires WithCustomId(...) unless it's a link button.");

        var json = new JsonObject
        {
            ["type"] = "button",
            ["style"] = _style.ToString().ToLowerInvariant(),
        };
        if (_customId is not null) json["customId"] = _customId;
        if (_url is not null) json["url"] = _url;
        if (_label is not null) json["label"] = _label;
        if (_emoji is not null) json["emoji"] = _emoji;
        if (_disabled) json["disabled"] = true;
        return json;
    }
}
