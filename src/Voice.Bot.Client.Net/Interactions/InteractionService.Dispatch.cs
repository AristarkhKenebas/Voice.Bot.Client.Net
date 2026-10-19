using System.Reflection;
using Microsoft.Extensions.Logging;
using Voice.Bot.Client.Net.Events;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Interactions;

/// <summary>Matching an incoming slash-command call to a registered handler and invoking it.</summary>
public sealed partial class InteractionService
{
    private async Task DispatchAsync(BotInteractionEvent interaction)
    {
        if (interaction.Type != InteractionType.ApplicationCommand || string.IsNullOrEmpty(interaction.Data.CommandName)) return;

        var handler = _handlers.FirstOrDefault(h => h.CommandName == interaction.Data.CommandName);
        if (handler is null)
        {
            // No module claims this command - not an error, raw InteractionCreated subscribers may still handle it.
            _logger.LogDebug("Unknown slash command /{Command} from {UserId}", interaction.Data.CommandName, interaction.InvokingUserId);
            return;
        }

        var context = new InteractionContext(_client, interaction);
        await _runner.RunAsync(context, handler.Metadata, _services, () => ExecuteAsync(handler, context),
            ex => RaiseHandlerFailedAsync(ex, interaction)).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(RegisteredHandler handler, InteractionContext context)
    {
        var args = BuildArguments(handler, context.Interaction);
        handler.Metadata.UseCooldown(context);
        await _modules.RunAsync<InteractionModuleBase>(handler.ModuleType, async module =>
        {
            module.Context = context;
            try
            {
                await CommandRunner.InvokeAsync(handler.Method, module, args).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!await module.OnErrorAsync(ex).ConfigureAwait(false)) throw;
            }
        }).ConfigureAwait(false);
    }

    /// <exception cref="CommandRejectedException">An option value doesn't convert to its parameter's type.</exception>
    private object?[] BuildArguments(RegisteredHandler handler, BotInteractionEvent interaction)
    {
        var parameters = handler.Method.GetParameters();
        if (parameters.Length == 0) return [];

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var name = GetEffectiveOptionName(parameters[i]);
            if (!interaction.Data.Options.TryGetValue(name, out var raw))
            {
                args[i] = ArgumentConverter.Missing(parameters[i]);
                continue;
            }

            try
            {
                args[i] = ArgumentConverter.Convert(raw, parameters[i].ParameterType);
            }
            catch (FormatException)
            {
                throw new CommandRejectedException(_client.Options.Replies.InvalidArgument(name, ArgumentConverter.Describe(parameters[i].ParameterType)));
            }
        }

        return args;
    }

    /// <summary>
    /// The option name a parameter is bound by, both when <see cref="RegisterCommandsAsync"/> declares
    /// it and when an incoming interaction's option values are matched back onto handler parameters -
    /// <see cref="SlashCommandOptionAttribute.Name"/> if set, else the CLR parameter name lowercased.
    /// </summary>
    private static string GetEffectiveOptionName(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<SlashCommandOptionAttribute>()?.Name ?? parameter.Name!.ToLowerInvariant();

    private async Task RaiseHandlerFailedAsync(Exception ex, BotInteractionEvent interaction)
    {
        var handler = HandlerFailed;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            await ((Func<Exception, BotInteractionEvent, Task>)d)(ex, interaction).ConfigureAwait(false);
        }
    }
}
