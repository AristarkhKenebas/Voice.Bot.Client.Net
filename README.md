# Voice.Bot.Client.Net

Официальная библиотека для C#/.NET для написания ботов мессенджера **Voice**: типизированный
клиент, модули со слэш-командами и текстовыми (`!ping`) командами через атрибуты, realtime-события
через gateway-подключение, и типизированные билдеры для кнопок/меню/модальных окон вместо ручного JSON.

Полное описание протокола (аутентификация, права, gRPC-методы, формат событий, схема
компонентов) — в документации Bot API: `docs/bots/overview` в
[Voice-LLC](https://github.com/Voice-LLC). Эта библиотека — референсная реализация того
протокола на .NET; если вы пишете бота на другом языке, документация протокола даст всё
необходимое для собственной реализации.

## Требования

- .NET 10 SDK
- Токен бота (`ibot_...`) — выдаётся владельцем бота при его создании в Voice

## Установка

Пакет пока не опубликован в NuGet — подключайте исходники напрямую:

```bash
git clone https://github.com/Voice-LLC/Voice.Bot.Client.git
```

и добавьте ссылку на проект:

```xml
<ItemGroup>
  <ProjectReference Include="..\Voice.Bot.Client\src\Voice.Bot.Client.Net\Voice.Bot.Client.Net.csproj" />
</ItemGroup>
```

## Быстрый старт

Самый простой способ — через `Microsoft.Extensions.Hosting` и встроенную DI-интеграцию:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Voice.Bot.Client.Net;
using Voice.Bot.Client.Net.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIoptaBotClient(opts =>
{
    opts.BotToken = "ibot_...";
});

builder.Services.AddInteractionModules();  // слэш-команды из [SlashCommand]-модулей
builder.Services.AddTextCommandModules();  // текстовые команды из [Command]-модулей

var host = builder.Build();
var client = host.Services.GetRequiredService<BotClient>();

client.MessageCreated += async message =>
{
    if (message.Author.IsBot) return;
    Console.WriteLine($"{message.Author.Username}: {message.Content}");
};

await host.RunAsync(); // подключает gateway, регистрирует слэш-команды, ждёт остановки
```

Без DI клиент собирается напрямую через `BotClient.Create(new BotClientOptions { BotToken = "..." })`.

## Слэш-команды

Модуль со слэш-командой — класс, унаследованный от `InteractionModuleBase`, с методами, помеченными `[SlashCommand]`:

```csharp
using Voice.Bot.Client.Net.Interactions;

public sealed class PingModule : InteractionModuleBase
{
    [SlashCommand("ping", "Отвечает pong")]
    public Task Ping() => RespondAsync("pong");

    [SlashCommand("echo", "Повторяет сказанное")]
    public Task Echo([SlashCommandOption("Что повторить", Required = true)] string message) =>
        RespondAsync(message);
}
```

`InteractionService.RegisterCommandsAsync()` сравнивает объявленные в коде команды с уже
зарегистрированными на бэкенде и синхронизирует разницу (массовая синхронизация, а не ручная
регистрация каждой команды по отдельности) — достаточно объявить команду в коде, отдельного шага
«выложить в консоли» не нужно.

## Текстовые команды (`!ping`)

Помимо слэш-команд, библиотека поддерживает классические префиксные команды: модуль наследуется
от `ModuleBase`, методы помечаются `[Command]`:

```csharp
using Voice.Bot.Client.Net.Commands;

public sealed class TextCommandsModule : ModuleBase
{
    [Command("ping")]
    [Alias("p")]
    public Task Ping() => ReplyAsync("pong");

    // Последний string-параметр «жадный» - !echo hello world свяжется как "hello world" целиком.
    [Command("echo")]
    public Task Echo(string text) => ReplyAsync(text);

    [Command("add")]
    public Task Add(int a, int b) => ReplyAsync($"{a + b}");
}
```

По умолчанию команды триггерятся префиксом `!` (сообщение `!ping`/`!p` вызовет `Ping()` выше) —
меняется через `BotClientOptions.TextCommandPrefix`. Аргументы разбиваются по пробелам, с
поддержкой `"строк в кавычках"` как одного токена. Бот никогда не реагирует на сообщения других
ботов (в том числе на свои собственные) — это встроенная защита от циклов.

Без DI то же самое собирается вручную:

```csharp
var commands = new CommandService(client, prefix: "!");
commands.AddModule<TextCommandsModule>();
commands.Initialize();
```

## Кнопки, меню и модальные окна

Компоненты сообщений собираются типизированными билдерами — писать JSON руками не нужно:

```csharp
using Voice.Bot.Client.Net.Components;

var components = new ComponentBuilder()
    .WithButton(b => b.WithStyle(ButtonStyle.Primary).WithCustomId("confirm").WithLabel("Подтвердить"))
    .WithComboBox(c => c.WithCustomId("pick").AddOption("Backend", "backend").AddOption("Frontend", "frontend"));

await client.SendMessageAsync(channelId, "Выберите вариант:", components: components);
```

Модальные окна — аналогично, через `ModalBuilder`, и открываются в ответ на слэш-команду или
клик по компоненту (`Context.RespondWithModalAsync(modal)`). Обработчики кликов и сабмитов —
методы модуля с `[ComponentInteraction("customId:*")]`/`[ModalInteraction("customId")]`.

## История сообщений

Требует intent `GuildMessages`. Даёт боту доступ к контексту канала, а не только к новым событиям
из Gateway:

```csharp
// Последние 50 сообщений канала, от старых к новым
var history = await client.GetChannelMessagesAsync(channelId, limit: 50);

// Пагинация назад - id самого старого полученного сообщения в beforeId
var older = await client.GetChannelMessagesAsync(channelId, limit: 50, beforeId: history[0].Id);

// Одно сообщение по id - например, чтобы узнать контекст InvokeMessageComponent
var message = await client.GetMessageAsync(messageId);
```

## Управление ролями и структурой группы

Бот с выданным intent'ом `Guilds` и правами `ManageChannels`/`ManageRoles` в конкретной группе
может создавать категории/каналы и управлять ролями:

```csharp
var category = await client.CreateCategoryAsync(groupId, "Bot Demo");
var channel = await client.CreateChannelAsync(category.Id, "bot-demo", ChannelType.Text);
var role = await client.CreateRoleAsync(groupId, "Muted", color: "#5865F2");
await client.AssignRoleAsync(groupId, targetUserId, role.Id);
```

Читать/переименовывать/удалять категории и каналы — `GetCategoryAsync`/`UpdateCategoryAsync`/
`DeleteCategoryAsync` и `UpdateChannelAsync`/`DeleteChannelAsync` (чтение категории не требует
`ManageChannels`, как и `GetChannelAsync` — структура группы видна любому участнику). Каждый метод
возвращает актуальное состояние сущности (id + поля), а `Delete*` — `{ Id, Success }`, а не пустой
ответ:

```csharp
var renamed = await client.UpdateChannelAsync(channel.Id, "bot-demo-renamed");
var deleted = await client.DeleteChannelAsync(channel.Id);
Console.WriteLine($"Удалён канал {deleted.Id}, success={deleted.Success}");
```

## Обработка ошибок

Любой неудачный вызов gRPC-метода выбрасывает `BotApiException` (никогда не «сырой»
`RpcException`) с `StatusCode`, по которому легко отличить нехватку intent'а/прав
(`PermissionDenied`) от неверных данных запроса (`InvalidArgument`) и так далее.

## Структура проекта

```
src/Voice.Bot.Client.Net/
  BotClient.cs, BotClient.*.cs   - facade к BotsApi, один partial-файл на группу методов
                                    (Profile/Voice/Messages/Groups/Roles/Interactions/SlashCommands/Gateway)
  Commands/                      - текстовые ("!ping") команды: CommandService, ModuleBase, [Command]/[Alias]
  Interactions/                  - слэш-команды: InteractionService, InteractionModuleBase, атрибуты
  Components/                    - типизированные билдеры кнопок/меню/чекбоксов/модальных окон
  Gateway/                       - SignalR-подключение к /hubs/bots
  Transport/                     - обёртки над сгенерированными gRPC-клиентами
  Models/, Events/                - DTO и события гейтвея
  Extensions/                    - DI-интеграция (AddIoptaBotClient, AddInteractionModules, AddTextCommandModules)
  Internal/                      - общие внутренние утилиты (например, привязка аргументов)
src/Voice.Bot.Client.Sample/     - консольный бот-пример (echo, слэш-команды, текстовые команды, компоненты)
tests/Voice.Bot.Client.Net.Tests/ - модульные тесты (моки транспорта/gateway, без реальной сети)
protos/                          - Protobuf-контракты (общие с бэкендом Voice)
```

## Тесты

```bash
dotnet test
```

## Ссылки

- Организация [Voice-LLC](https://github.com/Voice-LLC) — все официальные проекты Voice.
- Полная документация Bot API (для тех, кто пишет свою библиотеку на другом языке) — раздел
  «Боты» в документации Voice.
