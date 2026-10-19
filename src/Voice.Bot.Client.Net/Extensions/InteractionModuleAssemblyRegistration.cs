using System.Reflection;

namespace Voice.Bot.Client.Net.Extensions;

/// <summary>
/// Recorded by <see cref="ServiceCollectionExtensions.AddInteractionModules"/>, one per call. The <see cref="Interactions.InteractionService"/>
/// factory scans every recorded assembly when the service is first resolved.
/// </summary>
internal sealed record InteractionModuleAssemblyRegistration(Assembly Assembly);
