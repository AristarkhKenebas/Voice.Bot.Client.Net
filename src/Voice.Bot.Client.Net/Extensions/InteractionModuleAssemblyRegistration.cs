using System.Reflection;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Recorded by <see cref="ServiceCollectionExtensions.AddInteractionModules"/> - multiple registrations
/// (one per call, e.g. across assemblies) are resolved as
/// <c>IEnumerable&lt;InteractionModuleAssemblyRegistration&gt;</c> by <see cref="BotClientHostedService"/>
/// at startup, once an <see cref="Interactions.InteractionService"/> instance exists to scan into.
/// </summary>
internal sealed record InteractionModuleAssemblyRegistration(Assembly Assembly);
