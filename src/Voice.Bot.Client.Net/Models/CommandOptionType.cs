namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>interactions.CommandOptionType</c>.</summary>
public enum CommandOptionType
{
    String = 0,
    Integer = 1,
    Boolean = 2,
    User = 3,
    Channel = 4,
    Role = 5,
    Number = 6,
    SubCommand = 7,
    SubCommandGroup = 8,
}
