using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Channels.Grpc;
using Vc.Backend.WebApi.Groups.Grpc;
using Category = Voice.Bot.Client.Net.Models.Category;
using Channel = Voice.Bot.Client.Net.Models.Channel;

namespace Voice.Bot.Client.Net;

/// <summary>Categories, channels, their permission overwrites and voice channels (structure changes require <c>ManageChannels</c>).</summary>
public sealed partial class BotClient
{
    public async Task<Category> CreateCategoryAsync(Guid groupId, string name, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.CreateCategoryAsync(new CreateCategoryRequest { GroupId = groupId.ToString(), Name = name }, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();

    /// <summary>Renames a category.</summary>
    public async Task<Category> UpdateCategoryAsync(Guid categoryId, string name, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.UpdateCategoryAsync(new UpdateCategoryRequest { Id = categoryId.ToString(), Name = name }, o), ct).ConfigureAwait(false)).ToModel();

    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteCategoryAsync(new DeleteCategoryRequest { Id = categoryId.ToString() }, o), ct).ConfigureAwait(false);

    /// <summary>Moves a category between two neighbours; one of them is enough.</summary>
    public async Task<Category> MoveCategoryAsync(
        Guid categoryId, Guid? previousCategoryId = null, Guid? nextCategoryId = null, CancellationToken ct = default)
    {
        var request = new MoveCategoryRequest { CategoryId = categoryId.ToString() };
        if (previousCategoryId is { } previous) request.PreviousCategoryId = previous.ToString();
        if (nextCategoryId is { } next) request.NextCategoryId = next.ToString();
        return (await _api.CallAsync(o => _api.Groups.MoveCategoryAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task<Channel> CreateChannelAsync(Guid categoryId, string name, ChannelType type, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.CreateChannelAsync(
            new CreateChannelRequest { CategoryId = categoryId.ToString(), Name = name, Type = (int)type }, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();

    /// <summary>Changes a channel. A <c>null</c> argument leaves that field unchanged.</summary>
    /// <param name="userLimit">How many people fit into a voice channel, 0 for no limit.</param>
    public async Task UpdateChannelAsync(
        Guid channelId, string? name = null, string? description = null, int? userLimit = null, int? bitrate = null, CancellationToken ct = default)
    {
        var request = new UpdateChannelRequest { ChannelId = channelId.ToString() };
        if (name is not null) request.Name = name;
        if (description is not null) request.Description = description;
        if (userLimit is { } limit) request.UserLimit = limit;
        if (bitrate is { } rate) request.Bitrate = rate;
        await _api.CallAsync(o => _api.Channels.UpdateChannelAsync(request, o), ct).ConfigureAwait(false);
    }

    public async Task DeleteChannelAsync(Guid channelId, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteChannelAsync(new DeleteChannelRequest { Id = channelId.ToString() }, o), ct).ConfigureAwait(false);

    /// <summary>Moves a channel between two neighbours and, if <paramref name="targetCategoryId"/> is set, into another category.</summary>
    public async Task<Channel> MoveChannelAsync(
        Guid channelId, Guid? previousChannelId = null, Guid? nextChannelId = null, Guid? targetCategoryId = null, CancellationToken ct = default)
    {
        var request = new MoveChannelRequest { ChannelId = channelId.ToString() };
        if (previousChannelId is { } previous) request.PreviousChannelId = previous.ToString();
        if (nextChannelId is { } next) request.NextChannelId = next.ToString();
        if (targetCategoryId is { } category) request.TargetCategoryId = category.ToString();
        return (await _api.CallAsync(o => _api.Groups.MoveChannelAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Turns inheriting permissions from the category on or off.</summary>
    public async Task<Channel> SetChannelCategorySyncAsync(Guid channelId, bool synced, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.SetChannelCategorySyncAsync(
            new SetChannelCategorySyncRequest { ChannelId = channelId.ToString(), IsSynced = synced }, o), ct).ConfigureAwait(false)).ToModel();

    public async Task<IReadOnlyList<PermissionOverwrite>> GetChannelPermissionOverwritesAsync(Guid channelId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.GetChannelPermissionOverwritesAsync(
            new GetChannelPermissionOverwritesRequest { ChannelId = channelId.ToString() }, o), ct).ConfigureAwait(false)).Overwrites.Select(w => w.ToModel()).ToList();

    public async Task SetChannelPermissionOverwriteAsync(Guid channelId, PermissionOverwrite overwrite, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.SetChannelPermissionOverwriteAsync(new SetChannelPermissionOverwriteRequest
        {
            ChannelId = channelId.ToString(),
            TargetId = overwrite.TargetId.ToString(),
            IsRoleTarget = overwrite.IsRole,
            Allow = (ulong)overwrite.Allow,
            Deny = (ulong)overwrite.Deny,
        }, o), ct).ConfigureAwait(false);

    /// <param name="isRole"><paramref name="targetId"/> is a role id; otherwise it's a user id.</param>
    public async Task DeleteChannelPermissionOverwriteAsync(Guid channelId, Guid targetId, bool isRole, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteChannelPermissionOverwriteAsync(new DeleteChannelPermissionOverwriteRequest
        {
            ChannelId = channelId.ToString(), TargetId = targetId.ToString(), IsRoleTarget = isRole,
        }, o), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<PermissionOverwrite>> GetCategoryPermissionOverwritesAsync(Guid categoryId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Groups.GetCategoryPermissionOverwritesAsync(
            new GetCategoryPermissionOverwritesRequest { CategoryId = categoryId.ToString() }, o), ct).ConfigureAwait(false)).Overwrites.Select(w => w.ToModel()).ToList();

    public async Task SetCategoryPermissionOverwriteAsync(Guid categoryId, PermissionOverwrite overwrite, CancellationToken ct = default) =>
        await _api.CallAsync(o => _api.Groups.SetCategoryPermissionOverwriteAsync(new SetCategoryPermissionOverwriteRequest
        {
            CategoryId = categoryId.ToString(),
            TargetId = overwrite.TargetId.ToString(),
            IsRoleTarget = overwrite.IsRole,
            Allow = (ulong)overwrite.Allow,
            Deny = (ulong)overwrite.Deny,
        }, o), ct).ConfigureAwait(false);

    public async Task DeleteCategoryPermissionOverwriteAsync(Guid categoryId, Guid targetId, bool isRole, CancellationToken ct = default) =>
        await _api.DeleteAsync(o => _api.Groups.DeleteCategoryPermissionOverwriteAsync(new DeleteCategoryPermissionOverwriteRequest
        {
            CategoryId = categoryId.ToString(), TargetId = targetId.ToString(), IsRoleTarget = isRole,
        }, o), ct).ConfigureAwait(false);

    /// <summary>Who is in a voice channel right now.</summary>
    public async Task<IReadOnlyList<VoiceParticipant>> GetVoiceParticipantsAsync(Guid channelId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Channels.GetChannelUsersAsync(new GetChannelUsersRequest { ChannelId = channelId.ToString() }, o), ct).ConfigureAwait(false))
            .Users.Select(u => u.ToModel()).ToList();

    /// <summary>LiveKit address and token for joining a voice channel; audio itself goes through a LiveKit client.</summary>
    public async Task<VoiceConnection> JoinVoiceChannelAsync(Guid channelId, CancellationToken ct = default)
    {
        var response = await _api.CallAsync(o => _api.Channels.StartChannelConnectionAsync(new StartChannelConnectionRequest { ChannelId = channelId.ToString() }, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
        return new VoiceConnection(response.ServerUrl, response.Token);
    }
}
