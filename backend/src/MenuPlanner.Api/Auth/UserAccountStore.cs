using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Запись и чтение учётных записей по email/id: scoped-сервис, читающий БД.
/// Обработчики аутентификации получают его параметром, а не работают с
/// <see cref="AppDbContext"/> напрямую.
/// </summary>
public sealed class UserAccountStore
{
    private readonly AppDbContext _db;

    public UserAccountStore(AppDbContext db) => _db = db;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        _db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        _db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

    public void Add(User user) => _db.Users.Add(user);
}
