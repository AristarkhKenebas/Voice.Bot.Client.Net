using Voice.Bot.Client.Net.Internal;
using Voice.Bot.Client.Net.Models;
using Vc.Backend.WebApi.Interactions.Grpc;

namespace Voice.Bot.Client.Net;

/// <summary>
/// The bot's own slash commands. Calls arrive as <see cref="InteractionCreated"/> events. Most callers
/// use <see cref="SyncCommandsAsync"/> or <see cref="Interactions.InteractionService.RegisterCommandsAsync"/>,
/// which reconcile the registered commands with a list instead of calling the CRUD methods one by one.
/// </summary>
public sealed partial class BotClient
{
    public async Task<IReadOnlyList<BotCommandDefinition>> GetBotCommandsAsync(CancellationToken ct = default)
    {
        var botId = (await GetCachedMeAsync(ct).ConfigureAwait(false)).Id;
        return (await _api.CallAsync(o => _api.Interactions.GetBotCommandsAsync(new GetBotCommandsRequest { BotId = botId.ToString() }, o), ct).ConfigureAwait(false))
            .Commands.Select(c => c.ToModel()).ToList();
    }

    /// <summary>Commands of every bot in a group.</summary>
    public async Task<IReadOnlyList<BotCommandDefinition>> GetGroupCommandsAsync(Guid groupId, CancellationToken ct = default) =>
        (await _api.CallAsync(o => _api.Interactions.GetGroupCommandsAsync(new GetGroupCommandsRequest { GroupId = groupId.ToString() }, o), ct).ConfigureAwait(false))
            .Commands.Select(c => c.ToModel()).ToList();

    public async Task<BotCommandDefinition> RegisterCommandAsync(CommandDefinition definition, CancellationToken ct = default)
    {
        var request = new RegisterCommandRequest
        {
            BotId = (await GetCachedMeAsync(ct).ConfigureAwait(false)).Id.ToString(),
            Name = definition.Name,
            Description = definition.Description,
            DefaultMemberPermissions = (ulong)definition.DefaultMemberPermissions,
        };
        request.Options.AddRange((definition.Options ?? []).Select(o => o.ToWire()));
        return (await _api.CallAsync(o => _api.Interactions.RegisterCommandAsync(request, o), ct, Idempotency.Unsafe).ConfigureAwait(false)).ToModel();
    }

    /// <summary>Replaces a command's whole definition.</summary>
    public async Task<BotCommandDefinition> UpdateCommandAsync(Guid commandId, CommandDefinition definition, CancellationToken ct = default)
    {
        var request = new UpdateCommandRequest
        {
            BotId = (await GetCachedMeAsync(ct).ConfigureAwait(false)).Id.ToString(),
            CommandId = commandId.ToString(),
            Name = definition.Name,
            Description = definition.Description,
            DefaultMemberPermissions = (ulong)definition.DefaultMemberPermissions,
        };
        request.Options.AddRange((definition.Options ?? []).Select(o => o.ToWire()));
        return (await _api.CallAsync(o => _api.Interactions.UpdateCommandAsync(request, o), ct).ConfigureAwait(false)).ToModel();
    }

    public async Task DeleteCommandAsync(Guid commandId, CancellationToken ct = default)
    {
        var botId = (await GetCachedMeAsync(ct).ConfigureAwait(false)).Id;
        await _api.DeleteAsync(o => _api.Interactions.DeleteCommandAsync(
            new DeleteCommandRequest { BotId = botId.ToString(), CommandId = commandId.ToString() }, o), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the bot's commands match <paramref name="definitions"/>: registers new ones, updates changed
    /// ones and, when <paramref name="deleteMissing"/> is set, deletes the rest. Equal commands are left alone,
    /// so it's safe to call on every start.
    /// </summary>
    public async Task<CommandSyncResult> SyncCommandsAsync(
        IReadOnlyCollection<CommandDefinition> definitions, bool deleteMissing = true, CancellationToken ct = default)
    {
        var duplicates = definitions.GroupBy(d => d.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) throw new ArgumentException($"Duplicate command names: {string.Join(", ", duplicates)}.", nameof(definitions));

        var existing = await GetBotCommandsAsync(ct).ConfigureAwait(false);
        var existingByName = existing.ToDictionary(c => c.Name);
        var created = new List<BotCommandDefinition>();
        var updated = new List<BotCommandDefinition>();

        foreach (var definition in definitions)
        {
            if (!existingByName.TryGetValue(definition.Name, out var current))
            {
                created.Add(await RegisterCommandAsync(definition, ct).ConfigureAwait(false));
            }
            else if (current.ToDefinition() != definition)
            {
                updated.Add(await UpdateCommandAsync(current.Id, definition, ct).ConfigureAwait(false));
            }
        }

        var deleted = new List<BotCommandDefinition>();
        if (deleteMissing)
        {
            var wanted = definitions.Select(d => d.Name).ToHashSet();
            foreach (var stale in existing.Where(c => !wanted.Contains(c.Name)))
            {
                await DeleteCommandAsync(stale.Id, ct).ConfigureAwait(false);
                deleted.Add(stale);
            }
        }

        return new CommandSyncResult(created, updated, deleted);
    }
}
