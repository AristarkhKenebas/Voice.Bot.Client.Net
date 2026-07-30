using Voice.Bot.Client.Net.Components;
using Voice.Bot.Client.Net.Models;
using GrpcRespondToInteractionRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.RespondToInteractionRequest;
using GrpcSendInteractionFollowupRequest = Vc.Backend.WebApi.Bots.Apis.Grpc.SendInteractionFollowupRequest;
using GrpcInteractionResponseKind = Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionResponseKind;
using GrpcInteractionChoice = Vc.Backend.WebApi.Bots.Apis.Grpc.InteractionChoice;

namespace Voice.Bot.Client.Net;

/// <summary>
/// Answering an incoming interaction (slash command, component click, modal submit, autocomplete) -
/// no intent required, the server verifies the responding bot owns the interaction itself. Most callers
/// go through <see cref="Interactions.InteractionContext"/> instead of these two methods directly.
/// </summary>
public sealed partial class BotClient
{
    public Task RespondToInteractionAsync(Guid interactionId, InteractionResponse response, CancellationToken ct = default)
    {
        var request = new GrpcRespondToInteractionRequest
        {
            InteractionId = interactionId.ToString(),
            Kind = (GrpcInteractionResponseKind)(int)response.Kind,
            Content = response.Content ?? "",
            ComponentsJson = response.ComponentsJson ?? "",
            ModalJson = response.ModalJson ?? "",
            Ephemeral = response.Ephemeral,
        };
        if (response.AutocompleteChoices is not null)
        {
            request.AutocompleteChoices.AddRange(
                response.AutocompleteChoices.Select(c => new GrpcInteractionChoice { Name = c.Name, Value = c.Value }));
        }

        return _transport.RespondToInteractionAsync(request, ct);
    }

    public async Task<MessageInfo> SendInteractionFollowupAsync(
        Guid interactionId, string content, ComponentBuilder? components = null, CancellationToken ct = default)
    {
        var request = new GrpcSendInteractionFollowupRequest { InteractionId = interactionId.ToString(), Content = content };
        if (components is not null) request.ComponentsJson = components.Build();

        return (await _transport.SendInteractionFollowupAsync(request, ct)).ToModel();
    }
}
