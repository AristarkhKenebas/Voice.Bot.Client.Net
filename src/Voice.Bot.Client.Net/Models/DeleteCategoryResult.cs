using Wire = Vc.Backend.WebApi.Bots.Apis.Grpc.DeleteCategoryResponse;

namespace Voice.Bot.Client.Net.Models;

/// <summary>Mirrors <c>bots.DeleteCategoryResponse</c>.</summary>
public sealed record DeleteCategoryResult(Guid Id, bool Success);

internal static class DeleteCategoryResultExtensions
{
    public static DeleteCategoryResult ToModel(this Wire grpc) => new(Guid.Parse(grpc.Id), grpc.Success);
}
