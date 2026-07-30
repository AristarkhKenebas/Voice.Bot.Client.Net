namespace Voice.Bot.Client.Net.Events;

/// <summary>
/// <c>channel-events</c> payload wrapper for <c>"UserConnected"</c>/<c>"UserDisconnected"</c> - a user
/// joining/leaving a voice channel. <see cref="UserId"/> comes from the event envelope (a proper
/// <see cref="Guid"/> field on the wire) rather than the defensively-parsed <see cref="User"/>
/// snapshot, so it stays reliable even if the snapshot's own <c>Id</c> field ever fails to parse.
/// </summary>
public sealed record ChannelPresenceEvent(Guid GroupId, Guid ChannelId, Guid UserId, UserSnapshot User);
