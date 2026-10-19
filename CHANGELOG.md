# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/).

## [Unreleased]

The client now targets the new Voice API, where a bot is a regular user. This release is not
compatible with earlier builds.

### Changed

- `BotClient` calls the users, groups, channels, messages, attachments and interactions services instead
  of `BotsApi`, and returns plain models instead of protobuf types.
- Errors are `BotApiException` with `StatusCode`, `RetryAfter` and `IsRateLimited`; cancellation throws
  `OperationCanceledException`.
- Gateway events are handled one at a time, in arrival order; a handler that waits for an answer
  (`AskAsync`, `ConfirmAsync`, `WaitForAsync`, `CollectReactionsAsync`) stops holding up the events after it
  from the moment it starts waiting. The connection reconnects forever with growing pauses and retries the
  first connect; a revoked token stops it with `Unauthenticated`, a refused bot (HTTP 403) with
  `PermissionDenied`. Gateway errors never contain the bot token.
- Command modules get a DI scope per command and no longer have to be registered in the container.
- `AddIoptaBotClient` is now `AddVoiceBotClient` (the old name stays as an obsolete alias); the `appsettings`
  key is `Endpoint`.
- Permission bit masks are the `Permissions` flags enum everywhere (roles, overwrites, effective
  permissions, command default permissions); `Has` treats `Administrator` as every permission.
- Failed API calls are retried: rate limits for every call, network errors and timeouts only for calls
  that are safe to repeat (not for creating messages, uploads, roles, channels, invites or commands).
  Joining by invite and starting a voice connection aren't retried on network errors either; a delete
  that finds nothing to delete on a retry counts as done; a call the caller cancelled is never retried.
  Configure with `BotClientOptions.Retry` or per block with `client.WithRetries(...)`.
- Every request sends `User-Agent: voice-dotnet-sdk/<version>`.
- Text commands: arguments are split shell-style (single and double quotes, backslash escapes), the last
  `string` parameter takes the rest of the text exactly as typed (quotes, line breaks and spacing kept),
  parameters with a default value or a nullable type are optional, and a missing, malformed or extra
  argument is answered with the problem and a usage line instead of failing the command
  (`BotReplies.UnexpectedArgument = null` ignores extra arguments). Command names and aliases must be one
  word and unique (ignoring case when commands do); `AddModule` throws `ArgumentException` otherwise.
- Slash commands: an option value that doesn't convert is answered ("`days` must be a whole number.")
  and a missing one takes the parameter's default value. A duplicate command name throws
  `ArgumentException` from `AddModule` instead of failing the sync on start.
- A failing command is logged and answered with "Something went wrong." (rate-limit and permission errors
  get their own text; see `BotClientOptions.Replies`), besides raising `CommandExecutionFailed` /
  `HandlerFailed`.
- `CommandService` reads its prefixes from `BotClientOptions` unless one is passed to the constructor.
- `AddVoiceBotClient` falls back to the `VOICE_BOT_TOKEN` and `VOICE_PREFIX` environment variables, and the
  hosted service syncs the slash commands on start (`SyncCommands`), logging the names it created, updated
  and deleted. It connects in the background, so an unreachable server doesn't hold up the host's start;
  a token the gateway refuses stops the application.

### Added

- `RequestTimeout` (default 30 s) and `UploadTimeout` (default 5 min) options; infinite values mean no
  limit.
- `UploadFileAsync` for large files in parts, aborted on the server if anything fails.
- `SyncCommandsAsync` to make the bot's slash commands match a list.
- `BotClient` implements `IDisposable` as well as `IAsyncDisposable`. Disposing stops handling queued
  events, ends pending waits and gives running handlers (`RunMode.Async` ones too) a few seconds to finish.
- `BotContext`, the common base of `CommandContext` and `InteractionContext`: `ReplyAsync` (with files),
  `SendAsync`, `ReactAsync`, `DeleteAsync`, `EditAsync`, `BeginTyping`, `AskAsync` and `ConfirmAsync`. The
  answer to `AskAsync` is not also run as a text command.
- Checks as attributes on commands or modules: `[GroupOnly]`, `[DmOnly]`, `[OwnerOnly]`, `[Users]`,
  `[Roles]`, `[HasPermissions]`, `[BotHasPermissions]` and custom `CheckAttribute`s; `[Cooldown]` per user,
  channel, group or globally.
- Middleware for both command services (`Use`; calling `next()` twice still runs the command once), a
  per-module error handler (`OnErrorAsync`) and `RunMode.Async` for commands that shouldn't hold up other
  events. Cooldowns forget users once their window has passed.
- Text commands: several prefixes, the bot's mention as a prefix, descriptions, case-sensitivity option,
  `Guid` arguments from mentions, `bool`/`float`/enum arguments, and a built-in `help` listing text and slash
  commands.
- Slash command options of type user, channel and role (`[SlashCommandOption(Type = ...)]`), and
  `InteractionService.Commands`.
- `Ready` event (once per gateway connection, with the bot's profile) and the shortcuts `OnMessage`,
  `OnMemberJoin`, `OnMemberLeave`, `OnReactionAdd`, `OnReactionRemove` and `OnReady` with `EventFilter`.
- `WaitForAsync<T>` and `CollectReactionsAsync`; neither returns the bot's own messages or reactions.
- `Enumerate...Async` (`IAsyncEnumerable`) for members, bans, invites, roles, the audit log, message
  history and search results; they stop when the server repeats a page token.
- `BotClient.Cache`: groups, channels, roles and members, filled lazily and cleared by gateway events.
  Member events update the remembered member list instead of reloading it, and every gateway reconnection
  clears the cache.
- `Mention` and `Format` helpers: build and parse mentions, split long text (a code block cut in two is
  closed and reopened, an emoji is never cut in half), code blocks.
- `FileUpload`, `UploadAsync(FileUpload)`, `SendFileAsync` and `SendFilesAsync`, with the content type
  guessed from the extension.
- Scheduled tasks: `Every` (a fixed pause between runs, any length) and `At` (once a day, also across
  daylight-saving changes), started after the first `Ready`. They survive `StopGatewayAsync` and stop on
  disposal.
- `IBotModule`, `BotClient.Install` and `AddBotModule<T>` for reusable features.
- Optional client-side throttle (`BotClientOptions.Throttle`) and logging through `ILoggerFactory`.
- `Voice.Bot.Client.Net.Testing` package: `FakeVoiceServer`, an in-memory server to simulate messages, slash
  commands, reactions and members and check what the bot sent. Like the real server it answers a call
  before sending the events the call caused (the bot's own messages, role changes, kicks and bans), loses
  events while the bot is disconnected (`DisconnectAsync`, opt-in `QueueWhileDisconnected`) and requires a
  token; `FailNext(..., afterApplying: true)` fails a call after it did its work.

- `UserStatus.Unknown`, for a gateway message author without a presence status (was `Offline`).

### Removed

- Interaction responses, followups, components, modals, autocomplete, intents and leaving voice, which
  the new API does not have.
- The AI/OpenAI and Moq package references.

[Unreleased]: https://github.com/Voice-LLC/Voice.Bot.Client.Net/commits/HEAD
