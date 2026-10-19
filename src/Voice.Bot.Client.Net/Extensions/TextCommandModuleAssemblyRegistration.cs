using System.Reflection;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Recorded by <see cref="ServiceCollectionExtensions.AddTextCommandModules"/>, one per call. The <see cref="Commands.CommandService"/>
/// factory scans every recorded assembly when the service is first resolved.
/// </summary>
internal sealed record TextCommandModuleAssemblyRegistration(Assembly Assembly);
