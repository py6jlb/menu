using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Текущий пользователь и его членство: scoped-сервис, читающий БД. В отличие от
/// чистого <see cref="CurrentUser.UserId"/> (парсинг claim), зависит от
/// <see cref="AppDbContext"/> и внедряется в обработчики через DI.
/// </summary>
public sealed class CurrentUserContext
{
    private readonly AppDbContext _db;

    public CurrentUserContext(AppDbContext db) => _db = db;

    /// <summary>Пользователь по claim субъекта или null, если claim нет или пользователь удалён.</summary>
    public async Task<User?> UserAsync(ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return null;

        return await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId.Value);
    }

    /// <summary>Id семьи текущего пользователя или null, если он не состоит в семье.</summary>
    public async Task<Guid?> FamilyIdAsync(ClaimsPrincipal principal)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return null;

        var membership = await _db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        return membership?.FamilyId;
    }
}
