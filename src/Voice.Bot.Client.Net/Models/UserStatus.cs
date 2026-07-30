namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>common.UserStatus</c> (proto enum).</summary>
public enum UserStatus
{
    Online = 0,
    Offline = 1,
    DoNotDisturb = 2,
    Invisible = 3,
}

internal static class UserStatusExtensions
{
    public static UserStatus ToModel(this Vc.Backend.Contracts.Grpc.UserStatus grpc) => (UserStatus)(int)grpc;
    public static Vc.Backend.Contracts.Grpc.UserStatus ToGrpc(this UserStatus status) => (Vc.Backend.Contracts.Grpc.UserStatus)(int)status;
}
