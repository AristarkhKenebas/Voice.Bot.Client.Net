using System.Text.Json.Nodes;

namespace Voice.Bot.Client.Net.Components;

/// <summary>
/// Same option list/shape as <see cref="SelectMenuBuilder"/>, but rendered with a filter/type-ahead box
/// (<c>"searchable": true</c> in the wire schema, see docs/message-components.md in the Iopta.Backend
/// repo) - useful once the option list is too long to scan as a plain dropdown. Reports the chosen
/// value(s) back identically to a select menu.
/// </summary>
public sealed class ComboBoxBuilder : SelectMenuBuilder
{
    protected override string ComponentType => "combobox";

    internal override JsonObject ToJson()
    {
        var json = base.ToJson();
        json["searchable"] = true;
        return json;
    }
}
