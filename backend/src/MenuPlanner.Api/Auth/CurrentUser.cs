using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Общие хелперы эндпоинтов: кто вызывает запрос и в какой семье он состоит.
/// </summary>
public static class CurrentUser
{
    /// <summary>Id текущего пользователя из claim <c>sub</c> или null, если claim нет/некорректен.</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    /// <summary>Id семьи текущего пользователя или null, если он не состоит в семье.</summary>
    public static async Task<Guid?> FamilyIdAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserId(principal);
        if (userId is null)
            return null;

        var membership = await db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        return membership?.FamilyId;
    }
}
