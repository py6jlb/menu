using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth.Codes;

/// <summary>
/// Общий атомарный lifecycle одноразовых кодов: критическая секция по строке
/// пользователя, единственный действующий challenge на тип, выдача, выбор
/// последнего кода и фиксация транзакции. Предметные модули
/// (<see cref="EmailVerificationService"/>, <see cref="PasswordResetService"/>)
/// строят поверх него свои правила попыток, блокировки и потребления.
///
/// На реляционном провайдере операции над одним пользователем сериализуются
/// блокировкой его строки, поэтому конкурентные выдачи/потребления не теряют
/// попытки и не оставляют несколько действующих кодов. На нереляционном
/// провайдере (InMemory в быстрых тестах) шаг блокировки пропускается.
/// </summary>
public sealed class AuthCodeLifecycle
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IAuthCodeGenerator _generator;

    public AuthCodeLifecycle(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        IAuthCodeGenerator generator)
    {
        _db = db;
        _hasher = hasher;
        _generator = generator;
    }

    /// <summary>
    /// Открывает критическую секцию пользователя: транзакцию с блокировкой его
    /// строки. На реляционном провайдере строка перечитывается после блокировки,
    /// чтобы решение принималось по актуальному состоянию.
    /// </summary>
    public async Task<IDbContextTransaction?> BeginCriticalSectionAsync(
        User user, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
            return null;

        var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM \"Users\" WHERE \"Id\" = {0} FOR UPDATE",
            new object[] { user.Id }, ct);
        await _db.Entry(user).ReloadAsync(ct);
        return tx;
    }

    /// <summary>
    /// Выдаёт новый challenge указанного типа, закрывая все прежние неиспользованные.
    /// Вызывается внутри критической секции пользователя.
    /// </summary>
    public async Task<string> IssueAsync(
        Guid userId, AuthCodeType type, DateTime now, CancellationToken ct = default)
    {
        var unused = await _db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == type && !c.Used)
            .ToListAsync(ct);
        foreach (var stored in unused)
            AuthCodePolicy.Burn(stored);

        var code = _generator.Generate();
        _db.AuthCodes.Add(AuthCodePolicy.Create(_hasher, userId, type, code, now));
        return code;
    }

    /// <summary>
    /// Последний challenge указанного типа — единственный действующий, если он
    /// не был потреблён или закрыт. Потреблённый/закрытый возвращается, чтобы
    /// отличить «нет кода» от «код уже использован».
    /// </summary>
    public Task<AuthCode?> LatestAsync(
        Guid userId, AuthCodeType type, CancellationToken ct = default) =>
        _db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == type)
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Used)
            .FirstOrDefaultAsync(ct);

    public async Task SaveAndCommitAsync(
        IDbContextTransaction? tx, CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
    }
}
