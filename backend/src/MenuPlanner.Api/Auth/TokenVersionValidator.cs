using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

public static class TokenVersionValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Некорректный токен.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var currentVersion = await db.Users
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
        if (!int.TryParse(claim, out var tokenVersion) || tokenVersion != currentVersion.Value)
            context.Fail("Сессия устарела.");
    }
}
