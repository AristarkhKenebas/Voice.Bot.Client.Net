namespace Voice.Bot.Client.Net;

/// <summary>The outcome of a <see cref="CheckAttribute"/>.</summary>
/// <param name="Passed">Whether the command may run.</param>
/// <param name="Reply">What to tell the user when it may not; <c>null</c> to stay silent.</param>
public readonly record struct CheckResult(bool Passed, string? Reply)
{
    /// <summary>The command may run.</summary>
    public static CheckResult Success => new(true, null);

    /// <summary>The command may not run; <paramref name="reply"/> is sent to the user unless <c>null</c>.</summary>
    public static CheckResult Fail(string? reply) => new(false, reply);
}

/// <summary>
/// A condition a command must meet before it runs, as an attribute on a command method or on a whole
/// module class (then it covers every command in it). Works for text and slash commands alike. A failed
/// check answers with its reply and the handler is not called. Derive from it for custom checks.
/// </summary>
/// <example>
/// <code>
/// [GroupOnly, HasPermissions(Permissions.KickMembers)]
/// public sealed class ModerationModule : InteractionModuleBase { ... }
///
/// public sealed class WeekendOnlyAttribute : CheckAttribute
/// {
///     public override Task&lt;CheckResult&gt; CheckAsync(BotContext context, IServiceProvider? services) =&gt;
///         Task.FromResult(DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
///             ? CheckResult.Success
///             : Fail("Only on weekends!"));
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class CheckAttribute : Attribute
{
    /// <summary>Replaces the default answer when the check fails; empty to stay silent.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Decides whether the command may run.</summary>
    /// <param name="services">The application's services when the command framework uses dependency injection, otherwise <c>null</c>.</param>
    public abstract Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services);

    /// <summary>A failure answering with <see cref="ErrorMessage"/>, or <paramref name="defaultReply"/> when it isn't set.</summary>
    protected CheckResult Fail(string? defaultReply) =>
        CheckResult.Fail(ErrorMessage is null ? defaultReply : ErrorMessage.Length == 0 ? null : ErrorMessage);

    private protected static BotReplies Replies(BotContext context) => context.Client.Options.Replies;
}

/// <summary>Only inside a group, not in direct messages.</summary>
public sealed class GroupOnlyAttribute : CheckAttribute
{
    /// <inheritdoc />
    public override Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services) =>
        Task.FromResult(context.GroupId is null ? Fail(Replies(context).GroupOnly) : CheckResult.Success);
}

/// <summary>Only in direct messages.</summary>
public sealed class DmOnlyAttribute : CheckAttribute
{
    /// <inheritdoc />
    public override Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services) =>
        Task.FromResult(context.GroupId is null ? CheckResult.Success : Fail(Replies(context).DmOnly));
}

/// <summary>Only the owner of the group.</summary>
public sealed class OwnerOnlyAttribute : CheckAttribute
{
    /// <inheritdoc />
    public override async Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services)
    {
        if (context.GroupId is not { } groupId) return Fail(Replies(context).OwnerOnly);
        var group = await context.Client.Cache.GetGroupAsync(groupId).ConfigureAwait(false);
        return group.OwnerId == context.UserId ? CheckResult.Success : Fail(Replies(context).OwnerOnly);
    }
}

/// <summary>Only the listed users.</summary>
/// <param name="userIds">User ids, e.g. <c>[Users("2f1c...", "9a7e...")]</c>.</param>
public sealed class UsersAttribute(params string[] userIds) : CheckAttribute
{
    /// <summary>Who may use the command.</summary>
    public IReadOnlyCollection<Guid> UserIds { get; } = userIds.Select(Guid.Parse).ToHashSet();

    /// <inheritdoc />
    public override Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services) =>
        Task.FromResult(UserIds.Contains(context.UserId) ? CheckResult.Success : Fail(Replies(context).NotAllowed));
}

/// <summary>Only members who have at least one of the roles, given by name (ignoring case) or id.</summary>
/// <example><c>[Roles("Moderator", "Admin")]</c></example>
public sealed class RolesAttribute(params string[] roles) : CheckAttribute
{
    /// <summary>Role names or ids.</summary>
    public IReadOnlyList<string> Roles { get; } = roles;

    /// <inheritdoc />
    public override async Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services)
    {
        if (context.GroupId is not { } groupId) return Fail(Replies(context).GroupOnly);
        var member = await context.Client.Cache.GetMemberAsync(groupId, context.UserId).ConfigureAwait(false);
        if (member is null || member.RoleIds.Count == 0) return Fail(Replies(context).NotAllowed);

        var groupRoles = await context.Client.Cache.GetRolesAsync(groupId).ConfigureAwait(false);
        var wanted = groupRoles
            .Where(r => Roles.Any(name => string.Equals(name, r.Name, StringComparison.OrdinalIgnoreCase) || Guid.TryParse(name, out var id) && id == r.Id))
            .Select(r => r.Id)
            .ToHashSet();
        return member.RoleIds.Any(wanted.Contains) ? CheckResult.Success : Fail(Replies(context).NotAllowed);
    }
}

/// <summary>
/// Only members whose effective permissions in the channel include all of <see cref="Permissions"/>
/// (asked from the server on every use; <see cref="Net.Permissions.Administrator"/> counts as everything).
/// </summary>
/// <example><c>[HasPermissions(Permissions.KickMembers | Permissions.BanMembers)]</c></example>
public sealed class HasPermissionsAttribute(Permissions permissions) : CheckAttribute
{
    /// <summary>What the member needs.</summary>
    public Permissions Permissions { get; } = permissions;

    /// <inheritdoc />
    public override async Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services)
    {
        if (context.GroupId is not { } groupId) return Fail(Replies(context).GroupOnly);
        var effective = await context.Client.GetEffectivePermissionsAsync(groupId, context.UserId, context.ChannelId).ConfigureAwait(false);
        return effective.Has(Permissions) ? CheckResult.Success : Fail(Replies(context).MissingPermissions(effective.Missing(Permissions)));
    }
}

/// <summary>Only when the bot's own effective permissions in the channel include all of <see cref="Permissions"/>.</summary>
/// <example><c>[BotHasPermissions(Permissions.ManageMessages)]</c></example>
public sealed class BotHasPermissionsAttribute(Permissions permissions) : CheckAttribute
{
    /// <summary>What the bot needs.</summary>
    public Permissions Permissions { get; } = permissions;

    /// <inheritdoc />
    public override async Task<CheckResult> CheckAsync(BotContext context, IServiceProvider? services)
    {
        if (context.GroupId is not { } groupId) return Fail(Replies(context).GroupOnly);
        var effective = await context.Client.GetEffectivePermissionsAsync(groupId, channelId: context.ChannelId).ConfigureAwait(false);
        return effective.Has(Permissions) ? CheckResult.Success : Fail(Replies(context).BotMissingPermissions(effective.Missing(Permissions)));
    }
}

/// <summary>Who shares a <see cref="CooldownAttribute"/> budget.</summary>
public enum CooldownScope
{
    /// <summary>Each user separately.</summary>
    User = 0,

    /// <summary>Everyone in a channel together.</summary>
    Channel = 1,

    /// <summary>Everyone in a group together (in direct messages: the conversation).</summary>
    Group = 2,

    /// <summary>Everyone everywhere together.</summary>
    Global = 3,
}

/// <summary>
/// Allows a command <paramref name="uses"/> times per <paramref name="seconds"/> for each
/// <paramref name="scope"/>; beyond that it answers with the time left
/// (<see cref="BotReplies.Cooldown"/>). Only uses that pass the checks and argument parsing count.
/// </summary>
/// <example><c>[Cooldown(1, 10)]</c> - once per 10 seconds per user.</example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CooldownAttribute(int uses, double seconds, CooldownScope scope = CooldownScope.User) : Attribute
{
    /// <summary>How many uses fit in <see cref="Period"/>.</summary>
    public int Uses { get; } = uses > 0 ? uses : throw new ArgumentOutOfRangeException(nameof(uses), "Uses must be positive.");

    /// <summary>The window.</summary>
    public TimeSpan Period { get; } = seconds > 0 ? TimeSpan.FromSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds), "The period must be positive.");

    /// <summary>Who shares the budget.</summary>
    public CooldownScope Scope { get; } = scope;
}
