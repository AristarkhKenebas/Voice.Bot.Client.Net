using System.Reflection;

namespace Voice.Bot.Client.Net.Interactions;

internal enum HandlerKind { SlashCommand, Component, Modal, Autocomplete }

internal sealed record RegisteredHandler(HandlerKind Kind, string Match, string? OptionName, MethodInfo Method, Type ModuleType);
