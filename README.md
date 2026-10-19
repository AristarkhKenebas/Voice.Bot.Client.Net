# Voice.Bot.Client.Net

**Русский** · [English](README.en.md)

Официальная библиотека для C#/.NET для написания ботов мессенджера **Voice**: типизированный
клиент нового Voice API (бот — это обычный пользователь со своим токеном), модули со слэш-командами
и текстовыми (`!ping`) командами через атрибуты, проверки и кулдауны, realtime-события через
gateway-подключение, кэш, задачи по расписанию и пакет для модульных тестов бота без сети.

Полное описание протокола (аутентификация, права, gRPC-методы, формат событий) — в документации
Bot API: `docs/bots/overview` в [Voice-LLC](https://github.com/Voice-LLC). Эта библиотека —
референсная реализация того протокола на .NET; если вы пишете бота на другом языке, документация
протокола даст всё необходимое для собственной реализации.

## Требования

- .NET 10 SDK
- Токен бота (`ibot_...`) — выдаётся владельцем бота при его создании в Voice

## Установка

Пакет пока не опубликован в NuGet — подключайте исходники напрямую:

```bash
git clone https://github.com/Voice-LLC/Voice.Bot.Client.Net.git
```

и добавьте ссылку на проект:

```xml
<ItemGroup>
  <ProjectReference Include="..\Voice.Bot.Client.Net\src\Voice.Bot.Client.Net\Voice.Bot.Client.Net.csproj" />
  <!-- для Generic Host из быстрого старта: -->
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.*" />
</ItemGroup>
```

Для тестов бота есть отдельный проект `src/Voice.Bot.Client.Net.Testing` (см. [Тестирование бота](#тестирование-бота)).

## Быстрый старт

Самый простой способ — через `Microsoft.Extensions.Hosting` и встроенную DI-интеграцию:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddVoiceBotClient(opts =>
    {
        // Пустой токен - берётся из переменной окружения VOICE_BOT_TOKEN
        opts.BotToken = builder.Configuration["BotToken"] ?? "";
        opts.Endpoint = builder.Configuration["Endpoint"] ?? opts.Endpoint;
    })
    .AddInteractionModules()  // слэш-команды из [SlashCommand]-модулей
    .AddTextCommandModules(); // текстовые команды из [Command]-модулей

var host = builder.Build();
var client = host.Services.GetRequiredService<BotClient>();

client.OnReady(me =>
{
    Console.WriteLine($"Бот {me.Username} подключился");
    return Task.CompletedTask;
});

client.OnMessage(ctx =>
{
    Console.WriteLine($"{ctx.Author.Username}: {ctx.Message.Content}");
    return Task.CompletedTask;
});

await host.RunAsync(); // подключает gateway, синхронизирует слэш-команды, ждёт Ctrl+C / SIGTERM
```

`AddVoiceBotClient` регистрирует `BotClient`, `InteractionService`, `CommandService` и hosted service.
При старте хоста он устанавливает модули (`AddBotModule<T>`) и в фоне подключается к gateway (пока
сервер недоступен — повторяет попытки, хост не ждёт), затем синхронизирует слэш-команды и пишет в лог,
какие создал, обновил и удалил. Токен, который gateway отверг, останавливает приложение.

> `AddIoptaBotClient` — устаревший (`[Obsolete]`) псевдоним `AddVoiceBotClient`, оставлен для совместимости.

Без DI клиент собирается напрямую через `BotClient.Create`, а сервисы команд — вручную:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;

await using var client = BotClient.Create(BotClientOptions.FromEnvironment()); // VOICE_BOT_TOKEN

var interactions = new InteractionService(client);
interactions.AddModule<PingModule>();
interactions.Initialize();

var commands = new CommandService(client) { Interactions = interactions }; // !help покажет и слэш-команды
commands.AddModule<TextCommandsModule>();
commands.Initialize();

await client.StartGatewayAsync();
await interactions.RegisterCommandsAsync();

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
await stop.Task;
```

Полный рабочий пример — консольный бот в `src/Voice.Bot.Client.Sample`.

## Настройка

Токен и адрес сервера удобно держать в `appsettings.json` (так сделано в примере):

```json
{
  "BotToken": "",
  "Endpoint": "https://api.iopta.org",
  "AllowUntrustedCertificates": false
}
```

Переменные окружения, которые библиотека читает сама:

| Переменная | Что задаёт |
|---|---|
| `VOICE_BOT_TOKEN` | Токен бота, если `BotToken` пустой (в `AddVoiceBotClient`, `BotClient.Create` и `BotClientOptions.FromEnvironment()`) |
| `VOICE_PREFIX` | Префикс текстовых команд по умолчанию вместо `!` |

Остальное — свойства `BotClientOptions`:

| Свойство | По умолчанию | Назначение |
|---|---|---|
| `BotToken` | — | Токен бота (`ibot_...`) |
| `Endpoint` | `https://api.iopta.org` | Адрес Voice API, им пользуются и gRPC, и gateway (`/hubs/bots`) |
| `AllowUntrustedCertificates` | `false` | Принимать самоподписанный сертификат (локальный бэкенд) |
| `ConfigureHttpHandler` | `null` | Настройка `SocketsHttpHandler` (прокси, клиентские сертификаты) |
| `RequestTimeout` | 30 с | Лимит на один вызов API; `null` — без лимита |
| `UploadTimeout` | 5 мин | Лимит на загрузку файла (или одной его части) |
| `TextCommandPrefix` / `TextCommandPrefixes` | `!` | Один или несколько префиксов текстовых команд |
| `MentionPrefix` | `true` | Упоминание бота тоже работает как префикс: `@bot ping` |
| `CaseInsensitiveCommands` | `true` | `!PING` вызывает `ping` |
| `IgnoreBots` | `true` | Не реагировать на сообщения других ботов (свои игнорируются всегда) |
| `SyncCommands` | `true` | Синхронизировать слэш-команды при старте хоста |
| `HelpCommand` | `true` | Встроенная команда `help` |
| `EnableCache` | `true` | Кэш групп, каналов, ролей и участников (`client.Cache`) |
| `DefaultRunMode` | `RunMode.Sync` | Где выполняются команды по умолчанию |
| `Replies` | английские тексты | Что бот отвечает сам: ошибки, проверки, кулдауны, аргументы |
| `Retry` | 3 повтора | Повторы неудачных вызовов, `RetryOptions.None` — без повторов |
| `Throttle` | `null` | Клиентское ограничение частоты запросов (`new ThrottleOptions()` — 300 за 10 с) |

```csharp
builder.Services.AddVoiceBotClient(o =>
{
    o.TextCommandPrefixes = ["!", "?"];
    o.Retry = new RetryOptions { MaxRetries = 5 };
    o.Throttle = new ThrottleOptions();
    o.Replies.Error = "Упс, что-то пошло не так.";
    o.Replies.Cooldown = left => $"Не так быстро! Попробуйте через {BotReplies.FormatDuration(left)}.";
});
```

Логи клиент пишет через `ILoggerFactory` из контейнера (или переданный вторым аргументом в `BotClient.Create`).

## События

События gateway — обычные C#-события `BotClient` (`MessageCreated`, `MessageUpdated`, `MessageDeleted`,
`ReactionAdded`, `ReactionRemoved`, `UserTyping`, `MemberJoined`, `MemberLeft`, `MemberKicked`,
`GroupStructureChanged`, `UserProfileUpdated`, `UserStatusChanged`, `VoiceConnected`, `VoiceDisconnected`,
`InteractionCreated`, `Ready`, `ConnectionError` и другие). Поверх них есть короткие подписки `On...`: они
не присылают собственные сообщения и реакции бота, умеют фильтровать по каналам и группам и возвращают
`IDisposable` для отписки:

```csharp
client.OnMessage(async ctx =>
{
    if (ctx.Message.Content.Contains("привет", StringComparison.OrdinalIgnoreCase)) await ctx.ReactAsync("👋");
});

client.OnMemberJoin(async e =>
    await client.SendMessageAsync(channelId, $"Добро пожаловать, {Mention.User(e.UserId)}!"));

var subscription = client.OnReactionAdd(
    e => Task.CompletedTask,
    new EventFilter(ChannelIds: [channelId]));
subscription.Dispose(); // отписаться

client.MessageDeleted += e =>
{
    Console.WriteLine($"Удалено сообщение {e.MessageId}");
    return Task.CompletedTask;
};

client.ConnectionError += ex =>
{
    Console.WriteLine($"Ошибка gateway или обработчика: {ex.Message}");
    return Task.CompletedTask;
};
```

События обрабатываются по одному, в порядке прихода: обработчик создания сообщения завершается раньше,
чем начнётся обработчик его удаления. Поэтому долгую работу выносите в `Task.Run` или
`RunMode.Async`. Исключение — обработчик, который ждёт ответа (`AskAsync`, `ConfirmAsync`,
`WaitForAsync`, `CollectReactionsAsync`): с момента начала ожидания следующие события обрабатываются
параллельно. Соединение переподключается само, с растущими паузами; `Ready` приходит после каждого
подключения.

Дождаться конкретного события или собрать реакции:

```csharp
var answer = await client.WaitForAsync<MessageCreatedEvent>(
    e => e.Message.Author.Id == userId, TimeSpan.FromSeconds(30)); // TimeoutException, если не дождались

await foreach (var reaction in client.CollectReactionsAsync(messageId, TimeSpan.FromMinutes(1)))
{
    Console.WriteLine($"{reaction.UserId} поставил {reaction.Emoji}");
}
```

## Слэш-команды

Модуль со слэш-командой — класс, унаследованный от `InteractionModuleBase`, с методами, помеченными `[SlashCommand]`:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

public sealed class PingModule : InteractionModuleBase
{
    [SlashCommand("ping", "Отвечает pong")]
    public Task Ping() => ReplyAsync("pong");

    [SlashCommand("echo", "Повторяет сказанное")]
    public Task Echo([SlashCommandOption("Что повторить", Required = true)] string message) =>
        ReplyAsync(message);

    [SlashCommand("add", "Складывает два числа")]
    public Task Add(
        [SlashCommandOption("Первое число", Required = true)] int a,
        [SlashCommandOption("Второе число")] int b = 0) => ReplyAsync($"{a} + {b} = {a + b}");

    [SlashCommand("whois", "Показывает участника", DefaultMemberPermissions = Permissions.ManageRoles)]
    public Task WhoIs([SlashCommandOption("Кто", Required = true, Type = CommandOptionType.User)] Guid user) =>
        ReplyAsync($"Это {Mention.User(user)}");
}
```

Тип опции выводится из типа параметра: `string` — String, `int`/`long` — Integer, `bool` — Boolean,
`double`/`decimal` — Number (и их nullable-версии). Для пользователя, канала и роли укажите
`Type = CommandOptionType.User/Channel/Role` — значение придёт в параметр `Guid`. Имя опции по умолчанию —
имя параметра в нижнем регистре (`Name = "..."` меняет его). Необязательная опция, которую не заполнили,
получает значение параметра по умолчанию; значение, которое не удалось преобразовать, бот отклоняет
сам («`` `b` must be a whole number. ``») и обработчик не вызывает. Сырые значения — в `Context.Options`.

На новом API слэш-команда **отвечает обычным сообщением** в тот же канал: `ReplyAsync` в слэш-модуле
публикует сообщение (не ответ на interaction — такого вызова в API нет), `Context.MessageId` равен `null`.

`InteractionService.RegisterCommandsAsync()` сравнивает объявленные в коде команды с уже
зарегистрированными на бэкенде и синхронизирует разницу (создаёт новые, обновляет изменённые, удаляет
лишние) — достаточно объявить команду в коде, отдельного шага «выложить в консоли» не нужно. С Generic
Host это делается само при старте (`BotClientOptions.SyncCommands`). Без модулей список команд можно
синхронизировать напрямую:

```csharp
var result = await client.SyncCommandsAsync(
[
    new CommandDefinition("ping", "Отвечает pong"),
    new CommandDefinition("roll", "Бросает кубик",
        [new CommandOptionDefinition("sides", "Сколько граней", CommandOptionType.Integer)]),
]);
Console.WriteLine($"создано {result.Created.Count}, обновлено {result.Updated.Count}, удалено {result.Deleted.Count}");
```

Одинаковое имя у двух команд — `ArgumentException` из `AddModule` (а не ошибка синхронизации на старте).

## Текстовые команды (`!ping`)

Помимо слэш-команд, библиотека поддерживает классические префиксные команды: модуль наследуется
от `ModuleBase`, методы помечаются `[Command]`:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;

public sealed class TextCommandsModule : ModuleBase
{
    [Command("ping", Description = "Отвечает pong")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    // Последний string-параметр забирает весь остаток текста как есть - !echo hello world даст "hello world".
    [Command("echo", Description = "Повторяет сказанное")]
    public Task Echo(string text) => ReplyAsync(text);

    // Параметр со значением по умолчанию (или nullable) необязателен: "!add 2" ответит 2.
    [Command("add", Description = "Складывает два числа")]
    public Task Add(int a, int b = 0) => ReplyAsync($"{a + b}");

    [Command("color", Description = "Выбирает цвет")]
    public Task Color(ConsoleColor color) => ReplyAsync($"Выбран {color}");

    // Guid можно передать упоминанием: !avatar @user
    [Command("avatar", Description = "Ссылка на аватар")]
    public async Task Avatar(Guid user) =>
        await ReplyAsync(await Context.Client.GetAvatarUrlAsync(user) ?? "Аватара нет.");
}
```

По умолчанию команды триггерятся префиксом `!` (сообщение `!ping`/`!p` вызовет `Ping()` выше) —
меняется через `BotClientOptions.TextCommandPrefix(es)` или переменную `VOICE_PREFIX`; упоминание бота
(`@bot ping`) тоже работает как префикс. Аргументы разбиваются как в shell: одинарные и двойные
кавычки, экранирование обратным слэшем. Поддерживаются `string`, целые и дробные числа, `bool`, `Guid`
(в том числе из упоминания) и enum. Если аргумента не хватает, он не преобразуется или лишний, бот
отвечает, в чём проблема, и строкой использования вроде `` Usage: `!add <a> [b]` ``. Встроенная команда
`!help` перечисляет текстовые команды с описаниями и слэш-команды (`BotClientOptions.HelpCommand`;
своя команда `help` в модуле важнее). Бот никогда не реагирует на собственные сообщения, а по умолчанию
и на сообщения других ботов — это встроенная защита от циклов.

Без DI то же самое собирается вручную:

```csharp
var commands = new CommandService(client); // префиксы берутся из BotClientOptions
commands.AddModule<TextCommandsModule>();
commands.Initialize();
```

Модули создаются заново на каждую команду (с DI — в отдельном scope), поэтому могут принимать
зависимости через конструктор; регистрировать их в контейнере вручную не нужно.

## Проверки, кулдауны и middleware

Проверки — атрибуты на методе или на всём модуле, одинаково для текстовых и слэш-команд. Если проверка
не прошла, бот отвечает текстом из `BotClientOptions.Replies` (или `ErrorMessage` атрибута), а обработчик
не вызывается:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Models;

[GroupOnly]
public sealed class ModerationModule(BotClient client) : InteractionModuleBase
{
    [SlashCommand("kick", "Исключает участника из группы")]
    [HasPermissions(Permissions.KickMembers)]    // права участника в этом канале
    [BotHasPermissions(Permissions.KickMembers)] // и права самого бота
    public async Task Kick([SlashCommandOption("Кого", Required = true, Type = CommandOptionType.User)] Guid user)
    {
        await client.KickUserAsync(Context.GroupId!.Value, user);
        await ReplyAsync($"{Mention.User(user)} исключён.");
    }

    [SlashCommand("announce", "Объявление от имени бота")]
    [Roles("Moderator", "Admin")]
    [Cooldown(1, 60, CooldownScope.Group)] // раз в минуту на группу
    public Task Announce([SlashCommandOption("Текст", Required = true)] string text) => Context.SendAsync(text);

    // Ошибку команды можно обработать в модуле; true - ошибка обработана, стандартный ответ не нужен.
    protected override async Task<bool> OnErrorAsync(Exception error)
    {
        await ReplyAsync($"Команда {Context.CommandName} не удалась: {error.Message}");
        return true;
    }
}

public sealed class WeekendOnlyAttribute : CheckAttribute
{
    public override Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services) =>
        Task.FromResult(DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? CheckResult.Success
            : Fail("Только по выходным!"));
}
```

Встроенные проверки: `[GroupOnly]`, `[DmOnly]`, `[OwnerOnly]`, `[Users("<guid>", ...)]`,
`[Roles("имя или id", ...)]`, `[HasPermissions(...)]`, `[BotHasPermissions(...)]`. `[Cooldown(uses, seconds, scope)]`
ограничивает частоту на пользователя (по умолчанию), канал, группу или глобально; в счёт идут только
вызовы, прошедшие проверки и разбор аргументов.

Middleware оборачивает каждую команду (проверки, аргументы и обработчик); без вызова `next()` команда
не выполнится:

```csharp
using System.Diagnostics;

var commands = host.Services.GetRequiredService<CommandService>();
commands.Use(async (ctx, next) =>
{
    var started = Stopwatch.GetTimestamp();
    await next();
    Console.WriteLine($"{ctx.CommandName} заняла {Stopwatch.GetElapsedTime(started)}");
});

var interactions = host.Services.GetRequiredService<InteractionService>();
interactions.Use((ctx, next) => ctx.IsDirect ? ctx.ReplyAsync("Не в личке.") : next());
```

Долгую команду можно увести в фон, чтобы она не задерживала остальные события:
`[Command("report", RunMode = RunMode.Async)]` / `[SlashCommand(..., RunMode = RunMode.Async)]` (или
`BotClientOptions.DefaultRunMode`). Необработанная ошибка в команде пишется в лог, бот отвечает
`Replies.Error` («Something went wrong.»; для rate limit и нехватки прав — свои тексты), и поднимаются
события `CommandService.CommandExecutionFailed` / `InteractionService.HandlerFailed`.

## Контекст команды: ответы, файлы, «печатает…», вопросы

`Context` в обоих видах модулей (и `ctx` в `OnMessage`) — наследник `BotContext`: кто (`UserId`), где
(`ChannelId`, `GroupId`, `IsDirect`), какая команда (`CommandName`, `IsSlash`) и действия для ответа:

```csharp
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Commands;

public sealed class ProfileModule : ModuleBase
{
    [Command("profile", RunMode = RunMode.Async)]
    public async Task Profile()
    {
        var name = await Context.AskAsync("Как вас зовут?", TimeSpan.FromSeconds(30));
        if (name is null) { await ReplyAsync("Не дождался ответа."); return; }

        if (!await Context.ConfirmAsync($"Сохранить имя {name.Content}?")) return; // реакции ✅ / ❌

        await using (Context.BeginTyping()) // «печатает…», пока идёт работа
        {
            await Context.ReplyAsync("Готовлю отчёт...");
            await Context.EditAsync("Отчёт готов:");           // меняет последнее отправленное сообщение
            await ReplyAsync("Вот он", [FileUpload.FromPath("report.pdf")]);
        }

        await Context.ReactAsync("👍"); // реакция на сообщение с командой (только текстовые команды)
    }
}
```

- `ReplyAsync` — ответ на сообщение с командой (у слэш-команды — обычное сообщение в канал);
  `SendAsync` — сообщение в канал без ответа; у обоих есть перегрузка с файлами.
- `ReactAsync` и `DeleteAsync` работают с сообщением-командой; у слэш-команды сообщения нет, поэтому они
  бросают `InvalidOperationException`.
- `AskAsync` ждёт следующее сообщение того же пользователя в том же канале (по умолчанию минуту; `null`,
  если ответа нет). Ответ не запускается как текстовая команда, даже если выглядит как она.
- `ConfirmAsync` ставит под вопросом ✅ и ❌ и возвращает `true` только на ✅.

Файлы вне команд:

```csharp
await client.SendFileAsync(channelId, FileUpload.FromPath("chart.png"), "Сегодняшний график");
await client.SendFilesAsync(channelId, [FileUpload.FromBytes("hello.txt", "привет"u8.ToArray())]);

// Большой файл - частями, при ошибке загрузка отменяется на сервере
var video = await client.UploadFileAsync("video.mp4", "video/mp4");
await client.SendMessageAsync(channelId, "Видео", attachmentIds: [video.Id]);
```

Тип содержимого `FileUpload` угадывает по расширению (`FileUpload.GuessContentType`).

Упоминания и форматирование:

```csharp
var text = $"{Mention.User(userId)}, загляните в {Mention.Channel(channelId)}. {Format.Code("!help")}";
var mentioned = Mention.ParseUsers("<@...> и <@...>");                 // Guid'ы из текста
foreach (var part in Format.Split(longReport))                         // не длиннее 2000 символов
    await client.SendMessageAsync(channelId, part);
```

`Format.Split` не режет эмодзи пополам, а разрезанный блок кода закрывает и открывает заново.

## История сообщений и пагинация

Даёт боту доступ к контексту канала, а не только к новым событиям из Gateway:

```csharp
// Последние 50 сообщений канала, от старых к новым
var history = await client.GetChannelMessagesAsync(channelId, limit: 50);

// Пагинация назад - id самого старого полученного сообщения в beforeId
var older = await client.GetChannelMessagesAsync(channelId, limit: 50, beforeId: history[0].Id);

// Одно сообщение по id
var message = await client.GetMessageAsync(messageId);
```

Методы `Enumerate...Async` возвращают `IAsyncEnumerable` и сами ходят по страницам: участники, баны,
приглашения, роли, журнал аудита, история сообщений (от новых к старым) и результаты поиска:

```csharp
await foreach (var member in client.EnumerateGroupMembersAsync(groupId))
    Console.WriteLine(member.User.Username);

await foreach (var old in client.EnumerateMessagesAsync(channelId, limit: 500))
    if (old.Author.Id == userId) await client.DeleteMessageAsync(old.Id);
```

## Кэш

`client.Cache` лениво запоминает группы, каналы, роли и участников и очищает их по событиям gateway
(и целиком при каждом переподключении). Отключается `BotClientOptions.EnableCache = false`:

```csharp
var general = await client.Cache.GetChannelByNameAsync(groupId, "general");
var mods = await client.Cache.GetRoleByNameAsync(groupId, "Moderators");
var member = await client.Cache.GetMemberAsync(groupId, userId);
var groups = await client.Cache.GetGroupsAsync();
```

## Управление ролями и структурой группы

Бот с правами `ManageChannels`/`ManageRoles` в конкретной группе может создавать категории/каналы и
управлять ролями:

```csharp
var category = await client.CreateCategoryAsync(groupId, "Bot Demo");
var channel = await client.CreateChannelAsync(category.Id, "bot-demo", ChannelType.Text);
var role = await client.CreateRoleAsync(groupId, "Muted", color: "#5865F2");
await client.AssignRoleAsync(groupId, userId, role.Id);
```

Переименовывать/перемещать/удалять категории и каналы — `UpdateCategoryAsync`/`MoveCategoryAsync`/
`DeleteCategoryAsync` и `UpdateChannelAsync`/`MoveChannelAsync`/`DeleteChannelAsync`; структуру группы
целиком возвращает `GetGroupAsync`. Удаление, которого нечего удалять при повторной попытке, считается
выполненным:

```csharp
await client.UpdateChannelAsync(channelId, name: "bot-demo-renamed");
await client.DeleteChannelAsync(channelId);
```

Модерация — `KickUserAsync` (требует `Permissions.KickMembers`), `BanUserAsync`/`UnbanUserAsync`,
приглашения — `CreateInviteAsync`/`GetInvitesAsync`, журнал аудита — `GetAuditLogAsync`:

```csharp
await client.KickUserAsync(groupId, userId);
await client.BanUserAsync(groupId, userId, reason: "спам");
var invite = await client.CreateInviteAsync(groupId, expiresAt: DateTimeOffset.UtcNow.AddDays(1), maxUses: 10);
```

## Права

Битовые маски прав везде — флаговый enum `Permissions` (роли, переопределения каналов, итоговые права,
права по умолчанию для слэш-команд). `Has` считает `Administrator` всеми правами сразу:

```csharp
var moderator = Permissions.KickMembers | Permissions.BanMembers | Permissions.ManageMessages;
var effective = await client.GetEffectivePermissionsAsync(groupId, userId, channelId);
if (!effective.Has(moderator))
    Console.WriteLine($"Не хватает: {effective.Missing(moderator)}");

// без userId - права самого бота
var mine = await client.GetEffectivePermissionsAsync(groupId, channelId: channelId);

await client.SetChannelPermissionOverwriteAsync(channelId,
    new PermissionOverwrite(role.Id, IsRole: true, Deny: Permissions.SendMessages));
```

## Задачи по расписанию и модули

`Every` и `At` запускают задачи после первого `Ready`, переживают `StopGatewayAsync` и
останавливаются при `Dispose` клиента или возвращённого `IDisposable`. Упавший запуск пишется в лог и в
`ConnectionError`, расписание продолжается:

```csharp
client.Every(TimeSpan.FromMinutes(5), ct => client.UpdateProfileAsync(statusText: "Работаю", ct: ct));
client.At(new TimeOnly(9, 0), ct => client.SendMessageAsync(channelId, "Доброе утро!", ct: ct)); // каждый день в 9:00
```

`Every` выдерживает паузу между концом одного запуска и началом следующего, запуски не пересекаются.
`At` — раз в день по местному времени (или в `TimeZoneInfo`, переданной третьим аргументом), в том числе
при переходе на летнее время.

Обработчики и задачи, которые относятся к одной функции, удобно собрать в `IBotModule`:

```csharp
using Voice.Bot.Client.Net;

public sealed class WelcomeModule : IBotModule
{
    public void Install(BotClient client)
    {
        client.OnMemberJoin(async joined =>
        {
            var channel = await client.Cache.GetChannelByNameAsync(joined.GroupId, "welcome");
            if (channel is not null)
                await client.SendMessageAsync(channel.Id, $"Добро пожаловать, {Mention.User(joined.UserId)}!");
        });
    }
}
```

С DI — `builder.Services.AddBotModule<WelcomeModule>()` (модуль может принимать зависимости через
конструктор), без DI — `client.Install(new WelcomeModule())`.

## Повторы запросов и обработка ошибок

Любой неудачный вызов API выбрасывает `BotApiException` (никогда не «сырой» `RpcException`) с
`StatusCode`, по которому легко отличить нехватку прав (`PermissionDenied`) от неверных данных запроса
(`InvalidArgument`), отсутствующего объекта (`NotFound`) и так далее. `IsRateLimited` говорит, что сервер
ограничивает частоту запросов, `RetryAfter` — сколько он просит подождать. Если отменён
`CancellationToken`, переданный в метод, вылетает `OperationCanceledException`, а не `BotApiException`:

```csharp
using Grpc.Core;

try
{
    await client.SendMessageAsync(channelId, "привет", ct: ct);
}
catch (BotApiException ex) when (ex.IsRateLimited)
{
    await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5));
}
catch (BotApiException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
{
    Console.WriteLine("Боту не хватает прав в этом канале.");
}
catch (OperationCanceledException)
{
    // нас попросили остановиться
}
```

Неудачные вызовы повторяются сами (`BotClientOptions.Retry`, по умолчанию 3 раза с паузами 0,5 с, 1 с,
2 с...): rate limit — для любого метода, сетевые ошибки и таймауты — только для безопасных вызовов
(чтение и изменения, которые можно повторить). Создание сообщений, загрузки, ролей, каналов, приглашений
и команд при сетевой ошибке не повторяется, чтобы не получить дубль; отменённый вызов не повторяется
никогда. Для отдельного блока кода правила можно поменять:

```csharp
using (client.WithRetries(RetryOptions.None))
{
    await client.SendMessageAsync(channelId, "без повторов");
}
```

## Тестирование бота

Проект `Voice.Bot.Client.Net.Testing` — `FakeVoiceServer`, сервер Voice в памяти: `BotClient` без сети,
небольшой мир (группа с владельцем, участником и каналом `#general`), методы, имитирующие действия
пользователей, и журнал всего, что сделал бот. Каждый метод имитации возвращается, когда обработчики бота
закончили:

```csharp
using Voice.Bot.Client.Net.Commands;
using Voice.Bot.Client.Net.Interactions;
using Voice.Bot.Client.Net.Testing;
using Xunit;

public class BotTests
{
    [Fact]
    public async Task Ping_RepliesPong()
    {
        await using var server = new FakeVoiceServer();
        var commands = new CommandService(server.Client);
        commands.AddModule<TextCommandsModule>();
        commands.Initialize();

        await server.SendMessageAsync("!ping");

        Assert.Equal("pong", server.LastSentMessage?.Content);
    }

    [Fact]
    public async Task SlashAdd_UsesDefaultForMissingOption()
    {
        await using var server = new FakeVoiceServer(o => o.Replies.Error = "Ошибка");
        var interactions = new InteractionService(server.Client);
        interactions.AddModule<PingModule>();
        interactions.Initialize();

        await server.InvokeSlashCommandAsync("add", new Dictionary<string, string> { ["a"] = "2" });

        Assert.Equal("2 + 0 = 2", server.LastSentMessage?.Content);
    }
}
```

Кроме сообщений и слэш-команд можно имитировать реакции (`AddReactionAsync`), вход и выход участников
(`MemberJoinAsync`/`MemberLeaveAsync`), разрыв соединения (`DisconnectAsync`/`ConnectAsync`), а также
добавлять пользователей, каналы, роли и права (`AddUser`, `AddChannel`, `AddRole`, `SetPermissions`).
Сделанное ботом — в `SentMessages`, `Reactions`, `EditedMessages`, `DeletedMessages`, `Uploads`,
`Moderation`, `Calls`. `FailNext("CreateMessage", StatusCode.Unavailable)` заставляет следующий вызов
упасть (повторы у фейка выключены, включаются через `o.Retry` в конструкторе). Для `RunMode.Async` и
вопросов (`AskAsync`) — `WaitForSentMessageAsync`.

## Что не поддерживается новым API

В новом Voice API нет следующих возможностей, поэтому их нет и в библиотеке (они были в старых версиях):

- **ответов на interaction** (`RespondAsync`, отложенные ответы, followup-сообщения) — слэш-команда отвечает
  обычным сообщением в канал через `ReplyAsync`/`SendAsync`;
- **компонентов сообщений** — кнопок, меню и их обработчиков;
- **модальных окон**;
- **автодополнения** опций;
- **intents** — подписки на категории событий не настраиваются;
- **подкоманд и групп команд** — опции слэш-команд не вкладываются друг в друга;
- выхода из голосового канала.

## Структура проекта

```
src/Voice.Bot.Client.Net/
  BotClient.cs, BotClient.*.cs   - фасад к Voice API, один partial-файл на группу методов
                                    (Users/Groups/Channels/Roles/Messages/Attachments/Files/SlashCommands/
                                    Gateway/EventHelpers/Waiting/Pagination/Schedule)
  BotContext.cs, BotReplies.cs   - общий контекст команд и тексты автоответов
  Checks.cs                      - атрибуты-проверки и [Cooldown]
  BotCache.cs, Mention.cs, FileUpload.cs, Permissions.cs, RetryOptions.cs, IBotModule.cs
  Commands/                      - текстовые ("!ping") команды: CommandService, ModuleBase, [Command]/[Alias]
  Interactions/                  - слэш-команды: InteractionService, InteractionModuleBase, атрибуты
  Gateway/                       - SignalR-подключение к /hubs/bots
  Models/, Events/               - модели и события гейтвея
  Extensions/                    - DI-интеграция (AddVoiceBotClient, AddInteractionModules,
                                    AddTextCommandModules, AddBotModule) и hosted service
  Internal/                      - внутренние утилиты (вызовы API, повторы, загрузка файлов, привязка аргументов)
src/Voice.Bot.Client.Net.Testing/ - FakeVoiceServer для модульных тестов ботов
src/Voice.Bot.Client.Sample/     - консольный бот-пример (слэш- и текстовые команды, модерация, приветствия)
tests/Voice.Bot.Client.Net.Tests/ - модульные тесты (фейковый сервер, без реальной сети)
protos/                          - Protobuf-контракты (общие с бэкендом Voice)
```

Изменения между версиями — в [CHANGELOG.md](CHANGELOG.md).

## Тесты

```bash
dotnet test
```

## Ссылки

- Организация [Voice-LLC](https://github.com/Voice-LLC) — все официальные проекты Voice.
- Полная документация Bot API (для тех, кто пишет свою библиотеку на другом языке) — раздел
  «Боты» в документации Voice.
