using Google.Protobuf.WellKnownTypes;
using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Users.Grpc;
using User = Voice.Bot.Client.Net.Models.User;
using UserStatus = Voice.Bot.Client.Net.Models.UserStatus;
using WireUserStatus = Vc.Backend.WebApi.Users.Grpc.UserStatus;

namespace Voice.Bot.Client.Net;

/// <summary>The bot's own profile, presence, avatar and banner.</summary>
public sealed partial class BotClient
{
    /// <summary>The bot's own profile, fetched from the server on every call. <see cref="GetCachedMeAsync"/> remembers it.</summary>
    public async Task<User> GetMeAsync(CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Users.GetMeAsync(new Empty(), o), ct).ConfigureAwait(false)).ToModel();

    /// <summary>The bot's own profile, requested once and then remembered. A failed request is not remembered.</summary>
    public async Task<User> GetCachedMeAsync(CancellationToken ct = default)
    {
        await _meLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _me ??= await GetMeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _meLock.Release();
        }
    }

    /// <summary>The bot's id once its profile has been fetched, <see cref="Guid.Empty"/> before that.</summary>
    internal Guid CachedBotId => _me?.Id ?? Guid.Empty;

    /// <summary>Changes the display name, description or status line. A <c>null</c> argument leaves that field unchanged, an empty string clears it.</summary>
    public async Task<User> UpdateProfileAsync(
        string? displayName = null, string? description = null, string? statusText = null, CancellationToken ct = default)
    {
        var request = new UpdateUserRequest();
        if (displayName is not null) request.DisplayName = displayName;
        if (description is not null) request.Description = description;
        if (statusText is not null) request.Status = statusText;
        return (await _api.CallAsync(o => _api.Users.UpdateUserAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>
    /// Sets the bot's own presence status. Broadcasts live to every group the bot belongs to - see
    /// <see cref="UserStatusChanged"/> for the other side of that broadcast.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is <see cref="UserStatus.Unknown"/>.</exception>
    public async Task<User> SetStatusAsync(UserStatus status, CancellationToken ct = default)
    {
        if (status == UserStatus.Unknown) throw new ArgumentOutOfRangeException(nameof(status), "Unknown is not a status the bot can set.");
        return (await _api.CallAsync(o => _api.Users.UpdateUserStatusAsync(new UpdateUserStatusRequest { UserStatus = (WireUserStatus)(int)status }, o), ct).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Avatar URL of any user, <c>null</c> if they have none.</summary>
    public async Task<string?> GetAvatarUrlAsync(Guid userId, CancellationToken ct = default)
    {
        var response = await _api.CallAsync(o => _api.Users.GetUserAvatarAsync(new GetUserAvatarRequest { UserId = userId.ToString() }, o), ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(response.AvatarUrl) ? null : response.AvatarUrl;
    }

    /// <summary>Uploads a new avatar for the bot, e.g. a PNG or JPEG image.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is not a valid MIME type; nothing is sent.</exception>
    public async Task<User> SetAvatarAsync(ReadOnlyMemory<byte> image, string contentType, CancellationToken ct = default)
    {
        Uploader.ParseContentType(contentType);
        var ticket = await _api.CallAsync(
            o => _api.Users.InitiateAvatarUploadAsync(new InitiateAvatarUploadRequest { ContentType = contentType, SizeBytes = image.Length }, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
        await _uploader.PutAsync(ticket.UploadUrl, contentType, image, ct).ConfigureAwait(false);
        return (await _api.CallAsync(o => _api.Users.ConfirmAvatarUploadAsync(new ConfirmAvatarUploadRequest { AvatarId = ticket.AvatarId }, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task<User> DeleteAvatarAsync(CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Users.DeleteAvatarAsync(new Empty(), o), ct).ConfigureAwait(false)).ToModel();

    /// <summary>Uploads a new profile banner for the bot.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is not a valid MIME type; nothing is sent.</exception>
    public async Task<User> SetBannerAsync(ReadOnlyMemory<byte> image, string contentType, CancellationToken ct = default)
    {
        Uploader.ParseContentType(contentType);
        var ticket = await _api.CallAsync(
            o => _api.Users.InitiateBannerUploadAsync(new InitiateBannerUploadRequest { ContentType = contentType, SizeBytes = image.Length }, o), ct, Idempotency.Unsafe).ConfigureAwait(false);
        await _uploader.PutAsync(ticket.UploadUrl, contentType, image, ct).ConfigureAwait(false);
        return (await _api.CallAsync(o => _api.Users.ConfirmBannerUploadAsync(new ConfirmBannerUploadRequest { BannerId = ticket.BannerId }, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task<User> DeleteBannerAsync(CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Users.DeleteBannerAsync(new Empty(), o), ct).ConfigureAwait(false)).ToModel();
}
