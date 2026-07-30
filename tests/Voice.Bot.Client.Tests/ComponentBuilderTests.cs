using System.Text.Json.Nodes;
using FluentAssertions;
using Voice.Bot.Client.Components;
using Xunit;

namespace Voice.Bot.Client.Tests;

public class ComponentBuilderTests
{
    [Fact]
    public void Build_ShouldProduceActionRowWithButton()
    {
        var json = new ComponentBuilder()
            .WithButton(b => b.WithStyle(ButtonStyle.Danger).WithCustomId("cancel").WithLabel("Cancel"))
            .Build();

        var array = JsonNode.Parse(json)!.AsArray();
        array.Should().HaveCount(1);
        var row = array[0]!.AsObject();
        row["type"]!.GetValue<string>().Should().Be("actionRow");
        var component = row["components"]!.AsArray()[0]!.AsObject();
        component["type"]!.GetValue<string>().Should().Be("button");
        component["style"]!.GetValue<string>().Should().Be("danger");
        component["customId"]!.GetValue<string>().Should().Be("cancel");
        component["label"]!.GetValue<string>().Should().Be("Cancel");
    }

    [Fact]
    public void Build_LinkButton_ShouldOmitCustomId_AndIncludeUrl()
    {
        var json = new ComponentBuilder()
            .WithButton(b => b.WithStyle(ButtonStyle.Link).WithUrl("https://example.com").WithLabel("Docs"))
            .Build();

        var component = JsonNode.Parse(json)!.AsArray()[0]!["components"]!.AsArray()[0]!.AsObject();
        component["url"]!.GetValue<string>().Should().Be("https://example.com");
        component.ContainsKey("customId").Should().BeFalse();
    }

    [Fact]
    public void ButtonBuilder_ShouldThrow_WhenNonLinkButtonHasNoCustomId()
    {
        var act = () => new ComponentBuilder().WithButton(b => b.WithLabel("no id")).Build();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_ShouldProduceSelectMenu_WithOptionsAndValueRange()
    {
        var json = new ComponentBuilder()
            .WithSelectMenu(s => s
                .WithCustomId("pick")
                .WithPlaceholder("Choose one")
                .AddOption("Apple", "apple")
                .AddOption("Banana", "banana")
                .WithValueRange(1, 2))
            .Build();

        var component = JsonNode.Parse(json)!.AsArray()[0]!["components"]!.AsArray()[0]!.AsObject();
        component["type"]!.GetValue<string>().Should().Be("select");
        component["customId"]!.GetValue<string>().Should().Be("pick");
        component["placeholder"]!.GetValue<string>().Should().Be("Choose one");
        component["options"]!.AsArray().Should().HaveCount(2);
        component["minValues"]!.GetValue<int>().Should().Be(1);
        component["maxValues"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void Build_ShouldProduceComboBox_WithSearchableFlag()
    {
        var json = new ComponentBuilder()
            .WithComboBox(c => c.WithCustomId("search").AddOption("Alpha", "alpha"))
            .Build();

        var component = JsonNode.Parse(json)!.AsArray()[0]!["components"]!.AsArray()[0]!.AsObject();
        component["type"]!.GetValue<string>().Should().Be("combobox");
        component["searchable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Build_ShouldProduceCheckbox_WithCheckedState()
    {
        var json = new ComponentBuilder()
            .WithCheckbox(c => c.WithCustomId("agree").WithLabel("I agree").WithChecked())
            .Build();

        var component = JsonNode.Parse(json)!.AsArray()[0]!["components"]!.AsArray()[0]!.AsObject();
        component["type"]!.GetValue<string>().Should().Be("checkbox");
        component["checked"]!.GetValue<bool>().Should().BeTrue();
        component["label"]!.GetValue<string>().Should().Be("I agree");
    }

    [Fact]
    public void AddRow_ShouldGroupMultipleComponentsInOneRow()
    {
        var json = new ComponentBuilder()
            .AddRow(row => row
                .AddButton(b => b.WithCustomId("a").WithLabel("A"))
                .AddButton(b => b.WithCustomId("b").WithLabel("B")))
            .Build();

        var array = JsonNode.Parse(json)!.AsArray();
        array.Should().HaveCount(1);
        array[0]!["components"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void ModalBuilder_ShouldProduceTitleCustomIdAndComponents()
    {
        var json = new ModalBuilder()
            .WithTitle("Feedback")
            .WithCustomId("feedback-modal")
            .AddTextInput(t => t.WithCustomId("comment").WithLabel("Comment").WithStyle(TextInputStyle.Paragraph).WithRequired())
            .AddCheckbox(c => c.WithCustomId("subscribe").WithLabel("Notify me").WithChecked())
            .Build();

        var root = JsonNode.Parse(json)!.AsObject();
        root["title"]!.GetValue<string>().Should().Be("Feedback");
        root["customId"]!.GetValue<string>().Should().Be("feedback-modal");

        var rows = root["components"]!.AsArray();
        rows.Should().HaveCount(2);

        var textInput = rows[0]!["components"]!.AsArray()[0]!.AsObject();
        textInput["type"]!.GetValue<string>().Should().Be("textInput");
        textInput["style"]!.GetValue<string>().Should().Be("paragraph");
        textInput["required"]!.GetValue<bool>().Should().BeTrue();

        var checkbox = rows[1]!["components"]!.AsArray()[0]!.AsObject();
        checkbox["type"]!.GetValue<string>().Should().Be("checkbox");
        checkbox["checked"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ModalBuilder_ShouldThrow_WhenTitleOrCustomIdMissing()
    {
        var act = () => new ModalBuilder().AddTextInput(t => t.WithCustomId("x")).Build();

        act.Should().Throw<InvalidOperationException>();
    }
}
