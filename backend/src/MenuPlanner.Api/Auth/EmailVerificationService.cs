using Microsoft.AspNetCore.Identity;
using MenuPlanner.Api.Auth.Codes;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Auth;

public static class EmailVerificationService
{
    public static Task<string> IssueCodeAsync(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        Guid userId,
        DateTime now) =>
        AuthCodeIssuer.IssueAsync(db, hasher, userId, AuthCodeType.Verify, now);
}
