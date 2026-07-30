namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>common.BotTypingState</c> (proto enum).</summary>
public enum BotTypingState
{
    Thinking = 0,
    Explored = 1,
    Processing = 2,
    Reviewed = 3,
    Opened = 4,
    ReSearch = 5,
    Fetched = 6,
}

internal static class BotTypingStateExtensions
{
    public static BotTypingState ToModel(this Vc.Backend.Contracts.Grpc.BotTypingState grpc) => (BotTypingState)(int)grpc;
    public static Vc.Backend.Contracts.Grpc.BotTypingState ToGrpc(this BotTypingState state) => (Vc.Backend.Contracts.Grpc.BotTypingState)(int)state;
}
