using Voice.Bot.Client.Net.Models;
using GrpcGetGroupRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupRequest;
using GrpcGetGroupUsersRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetGroupUsersRequest;
using GrpcGetChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.GetChannelRequest;
using GrpcCreateCategoryRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateCategoryRequest;
using GrpcCreateChannelRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.CreateChannelRequest;

namespace Voice.Bot.Client.Net;

/// <summary>Groups/channels the bot belongs to, plus creating structure within them - requires the <c>Guilds</c> intent.</summary>
public sealed partial class BotClient
{
    public async Task<IReadOnlyList<BotGroupInfo>> GetMyGroupsAsync(CancellationToken ct = default) =>
        (await _transport.GetMyGroupsAsync(ct)).Groups.Select(g => g.ToModel()).ToList();

    public async Task<BotGroupDetails> GetGroupAsync(Guid groupId, CancellationToken ct = default) =>
        (await _transport.GetGroupAsync(new GrpcGetGroupRequest { GroupId = groupId.ToString() }, ct)).ToModel();

    public async Task<IReadOnlyList<User>> GetGroupUsersAsync(Guid groupId, CancellationToken ct = default) =>
        (await _transport.GetGroupUsersAsync(new GrpcGetGroupUsersRequest { GroupId = groupId.ToString() }, ct)).Users.Select(u => u.ToModel()).ToList();

    public async Task<BotChannelDetails> GetChannelAsync(Guid channelId, CancellationToken ct = default) =>
        (await _transport.GetChannelAsync(new GrpcGetChannelRequest { ChannelId = channelId.ToString() }, ct)).ToModel();

    /// <summary>
    /// Creates a category in a group this bot belongs to (requires the <c>Guilds</c> intent and
    /// <c>ManageChannels</c> in the bot's managed role for that group). Creating the group/server itself
    /// is deliberately not exposed to bots - only structure within an existing one.
    /// </summary>
    public async Task<BotCategoryInfo> CreateCategoryAsync(Guid groupId, string name, CancellationToken ct = default) =>
        (await _transport.CreateCategoryAsync(new GrpcCreateCategoryRequest { GroupId = groupId.ToString(), Name = name }, ct)).ToModel();

    /// <summary>Creates a channel under a category in a group this bot belongs to (requires <c>Guilds</c> intent + <c>ManageChannels</c>).</summary>
    public async Task<BotChannelInfo> CreateChannelAsync(Guid categoryId, string name, ChannelType type, CancellationToken ct = default) =>
        (await _transport.CreateChannelAsync(
            new GrpcCreateChannelRequest { CategoryId = categoryId.ToString(), Name = name, Type = (int)type }, ct)).ToModel();
}
