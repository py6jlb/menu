using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public static class EmailVerificationService
{
    public static async Task<string> IssueCodeAsync(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        Guid userId,
        DateTime now)
    {
        var unused = await db.AuthCodes
            .Where(c => c.UserId == userId && c.Type == AuthCodeType.Verify && !c.Used)
            .ToListAsync();
        foreach (var stored in unused)
            AuthCodeService.Burn(stored);

        var code = AuthCodeService.GenerateCode();
        db.AuthCodes.Add(AuthCodeService.Create(hasher, userId, AuthCodeType.Verify, code, now));
        await db.SaveChangesAsync();
        return code;
    }
}
