using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class AuthSessionValidatorTests
{
    [Theory]
    [InlineData("3", 3, true)]
    [InlineData("2", 3, false)]
    [InlineData(null, 3, false)]
    [InlineData("not-a-number", 3, false)]
    [InlineData("", 0, false)]
    public void IsCurrent_ComparesParsedVersion(string? claim, int current, bool expected)
    {
        Assert.Equal(expected, AuthSessionValidator.IsCurrent(claim, current));
    }

    [Fact]
    public async Task ValidateAsync_AcceptsCurrentVersion_AndRejectsStale()
    {
        await using var db = NewDb();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "session@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            TokenVersion = 2
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var validator = new AuthSessionValidator(db);

        var current = Context(Principal(user.Id, "2"));
        await validator.ValidateAsync(current);
        Assert.Null(current.Result);

        var stale = Context(Principal(user.Id, "1"));
        await validator.ValidateAsync(stale);
        Assert.NotNull(stale.Result);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal Principal(Guid userId, string tokenVersion) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtTokenService.TokenVersionClaim, tokenVersion)
        }));

    private static TokenValidatedContext Context(ClaimsPrincipal principal)
    {
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(new DefaultHttpContext(), scheme, new JwtBearerOptions())
        {
            Principal = principal
        };
    }
}
