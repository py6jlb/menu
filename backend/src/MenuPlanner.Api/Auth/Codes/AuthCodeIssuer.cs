using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth.Codes;

public static class AuthCodeIssuer
{
    public static async Task<string> IssueAsync(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        Guid userId,
        AuthCodeType type,
        DateTime now)
    {
        var unused = await db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == type && !c.Used)
            .ToListAsync();
        foreach (var stored in unused)
            AuthCodeService.Burn(stored);

        var code = AuthCodeService.GenerateCode();
        db.AuthCodes.Add(AuthCodeService.Create(hasher, userId, type, code, now));
        await db.SaveChangesAsync();
        return code;
    }
}
