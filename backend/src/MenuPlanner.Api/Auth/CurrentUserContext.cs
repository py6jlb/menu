using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Членство текущего пользователя: scoped-сервис, читающий БД. В отличие от
/// чистого <see cref="CurrentUser.UserId"/> (парсинг claim), зависит от
/// <see cref="AppDbContext"/> и внедряется в обработчики через DI.
/// </summary>
public sealed class CurrentUserContext
{
    private readonly AppDbContext _db;

    public CurrentUserContext(AppDbContext db) => _db = db;

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
