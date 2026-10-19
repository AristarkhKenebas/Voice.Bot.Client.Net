using System.Reflection;
using Voice.Bot.Client.Net.Internal;

namespace Voice.Bot.Client.Net.Interactions;

internal sealed record RegisteredHandler(string CommandName, MethodInfo Method, Type ModuleType, CommandMetadata Metadata);
