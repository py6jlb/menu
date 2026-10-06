using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Emails.Outbox;

namespace MenuPlanner.Api.Auth;

public enum AdminBootstrapOutcome
{
    /// <summary>Первый администратор создан и получил письмо с кодом.</summary>
    Created,

    /// <summary>Администратор создан, но письмо с кодом доставить не удалось.</summary>
    CreatedEmailFailed,

    /// <summary>Администратор уже существует: ничего не изменено.</summary>
    AlreadyInitialized,

    /// <summary>Email занят обычным пользователем: ничего не изменено.</summary>
    EmailTaken,

    /// <summary>Некорректный email или слишком короткий пароль.</summary>
    InvalidInput
}

public readonly record struct AdminBootstrapResult(AdminBootstrapOutcome Outcome, Guid? UserId);

/// <summary>
/// Явное первоначальное назначение администратора, вызываемое оператором вне
/// публичного регистрационного маршрута (CLI-команда <c>admin-bootstrap</c>).
///
/// Публичная регистрация всегда выдаёт роль <see cref="UserRole.User"/>, поэтому
/// первый случайный посетитель и конкурентные запросы не могут занять системную
/// роль. Bootstrap создаёт ровно одного первоначального администратора:
/// конкурентные запуски сериализуются advisory-блокировкой PostgreSQL, повторный
/// запуск не создаёт второго администратора и не перезаписывает пароль
/// существующего пользователя. Созданный администратор остаётся неподтверждённым
/// и получает код по общим правилам подтверждения почты.
/// </summary>
public sealed class AdminBootstrap
{
    /// <summary>
    /// Ключ advisory-блокировки PostgreSQL: ASCII "MENUADMN". Держится на время
    /// транзакции создания, поэтому конкурентные bootstrap не видят состояние
    /// друг друга частично применённым.
    /// </summary>
    internal const long AdvisoryLockKey = 0x4D454E5541444D4E;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly EmailVerificationService _verification;
    private readonly EmailOutboxProcessor _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdminBootstrap> _logger;

    public AdminBootstrap(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        EmailVerificationService verification,
        EmailOutboxProcessor outbox,
        TimeProvider clock,
        ILogger<AdminBootstrap> logger)
    {
        _db = db;
        _hasher = hasher;
        _verification = verification;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AdminBootstrapResult> RunAsync(
        string? emailInput, string? passwordInput, CancellationToken ct = default)
    {
        var email = EmailPolicy.Normalize(emailInput);
        if (!EmailPolicy.IsValid(email))
            return new AdminBootstrapResult(AdminBootstrapOutcome.InvalidInput, null);

        var password = passwordInput ?? "";
        if (password.Length < PasswordPolicy.MinLength)
            return new AdminBootstrapResult(AdminBootstrapOutcome.InvalidInput, null);

        var created = await CreateInitialAdminAsync(email, password, ct);
        if (created.Outcome != AdminBootstrapOutcome.Created || created.UserId is null)
            return created;

        var user = await _db.Users.FirstAsync(u => u.Id == created.UserId, ct);

        try
        {
            await _verification.IssueInitialCodeAsync(user, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Bootstrap администратора: не удалось выдать код подтверждения.");
            return new AdminBootstrapResult(AdminBootstrapOutcome.CreatedEmailFailed, user.Id);
        }

        // Команда одноразовая: сразу доводим письмо, но при временном отказе
        // запись остаётся в очереди и будет повторена при следующем запуске.
        try
        {
            await _outbox.DispatchDueAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Bootstrap администратора: отправка письма из очереди не удалась.");
        }

        var stillQueued = await _db.EmailOutboxMessages.AnyAsync(m => m.UserId == user.Id, ct);
        return new AdminBootstrapResult(
            stillQueued ? AdminBootstrapOutcome.CreatedEmailFailed : AdminBootstrapOutcome.Created,
            user.Id);
    }

    private async Task<AdminBootstrapResult> CreateInitialAdminAsync(
        string email, string password, CancellationToken ct)
    {
        if (!_db.SupportsRelationalLocking())
            return await CreateAsync(email, password, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", new object[] { AdvisoryLockKey }, ct);

        var result = await CreateAsync(email, password, ct);
        if (result.Outcome == AdminBootstrapOutcome.Created)
            await tx.CommitAsync(ct);
        else
            await tx.RollbackAsync(ct);

        return result;
    }

    private async Task<AdminBootstrapResult> CreateAsync(
        string email, string password, CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct))
            return new AdminBootstrapResult(AdminBootstrapOutcome.AlreadyInitialized, null);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return new AdminBootstrapResult(AdminBootstrapOutcome.EmailTaken, null);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = _hasher.HashPassword(null!, password),
            Role = UserRole.Admin,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return new AdminBootstrapResult(AdminBootstrapOutcome.Created, user.Id);
    }
}
