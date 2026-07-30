using System.Reflection;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Recorded by <see cref="ServiceCollectionExtensions.AddTextCommandModules"/> - multiple registrations
/// (one per call, e.g. across assemblies) are resolved as
/// <c>IEnumerable&lt;TextCommandModuleAssemblyRegistration&gt;</c> by <see cref="BotClientHostedService"/>
/// at startup, once a <see cref="Commands.CommandService"/> instance exists to scan into.
/// </summary>
internal sealed record TextCommandModuleAssemblyRegistration(Assembly Assembly);
