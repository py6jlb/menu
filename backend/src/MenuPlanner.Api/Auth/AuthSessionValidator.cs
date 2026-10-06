using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Проверяет, что версия токена сессии совпадает с текущей у пользователя.
/// Scoped-сервис: событие JwtBearer получает его через <c>RequestServices</c>,
/// а не достаёт <see cref="AppDbContext"/> из контейнера вручную.
/// </summary>
public sealed class AuthSessionValidator
{
    private readonly AppDbContext _db;

    public AuthSessionValidator(AppDbContext db) => _db = db;

    /// <summary>Чистое правило: версия из claim парсится и совпадает с текущей.</summary>
    public static bool IsCurrent(string? tokenVersionClaim, int currentVersion) =>
        int.TryParse(tokenVersionClaim, out var tokenVersion) && tokenVersion == currentVersion;

    public async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Некорректный токен.");
            return;
        }

        var currentVersion = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.TokenVersion)
            .FirstOrDefaultAsync();

        if (currentVersion is null)
        {
            context.Fail("Пользователь не найден.");
            return;
        }

        var claim = principal!.FindFirstValue(JwtTokenService.TokenVersionClaim);
        if (!IsCurrent(claim, currentVersion.Value))
            context.Fail("Сессия устарела.");
    }
}
