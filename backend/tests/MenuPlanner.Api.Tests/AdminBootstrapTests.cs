using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public sealed class AdminBootstrapTests
{
    [Fact]
    public async Task Bootstrap_OnCleanInstall_CreatesUnverifiedAdminAndSendsCode()
    {
        using var factory = new AuthApiFactory();
        using var scope = factory.Services.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();

        var result = await bootstrap.RunAsync("Admin@Example.com", "secret1");

        Assert.Equal(AdminBootstrapOutcome.Created, result.Outcome);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.SingleAsync();
        Assert.Equal("admin@example.com", admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.False(admin.IsEmailVerified);
        Assert.False(string.IsNullOrEmpty(admin.PasswordHash));

        Assert.Single(factory.Emails);
        Assert.Contains("Код подтверждения почты", factory.Emails[0].Subject);
    }

    [Fact]
    public async Task BootstrapAdmin_VerifiesEmailThroughCommonFlow()
    {
        const string email = "admin-verify@example.com";
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
            var result = await bootstrap.RunAsync(email, "secret1");
            Assert.Equal(AdminBootstrapOutcome.Created, result.Outcome);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal("Admin", auth.User.Role);
        Assert.False(auth.User.IsEmailVerified);

        var letter = Assert.Single(factory.Emails);
        var match = Regex.Match(letter.HtmlBody, @"class=""code"">(\d{6})<");
        Assert.True(match.Success, "Код не найден в теле письма.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/verify");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        request.Content = JsonContent.Create(new { code = match.Groups[1].Value });
        using var verify = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verified = await verify.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(verified);
        Assert.True(verified.IsEmailVerified);
    }

    [Fact]
    public async Task Bootstrap_Repeat_DoesNotCreateSecondAdminOrOverwritePassword()
    {
        using var factory = new AuthApiFactory();
        using var scope = factory.Services.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = await bootstrap.RunAsync("admin@example.com", "secret1");
        Assert.Equal(AdminBootstrapOutcome.Created, first.Outcome);

        var second = await bootstrap.RunAsync("admin@example.com", "other-password");
        Assert.Equal(AdminBootstrapOutcome.AlreadyInitialized, second.Outcome);

        Assert.Equal(1, await db.Users.CountAsync());
        var admin = await db.Users.SingleAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, "secret1"));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, "other-password"));
    }

    [Fact]
    public async Task Bootstrap_WithExistingAdmin_PreservesItAndRefusesToCreateSecond()
    {
        using var factory = new AuthApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
        var result = await bootstrap.RunAsync("another@example.com", "secret1");

        Assert.Equal(AdminBootstrapOutcome.AlreadyInitialized, result.Outcome);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
        Assert.Equal("existing@example.com", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task Bootstrap_WhenEmailBelongsToRegularUser_LeavesUserUntouched()
    {
        using var factory = new AuthApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "taken@example.com",
            PasswordHash = hasher.HashPassword(null!, "secret1"),
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
        var result = await bootstrap.RunAsync("taken@example.com", "other-password");

        Assert.Equal(AdminBootstrapOutcome.EmailTaken, result.Outcome);
        var user = await db.Users.SingleAsync();
        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, user.PasswordHash, "secret1"));
    }

    [Theory]
    [InlineData("not-an-email", "secret1")]
    [InlineData("admin@example.com", "123")]
    public async Task Bootstrap_InvalidInput_ChangesNothing(string email, string password)
    {
        using var factory = new AuthApiFactory();
        using var scope = factory.Services.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();

        var result = await bootstrap.RunAsync(email, password);

        Assert.Equal(AdminBootstrapOutcome.InvalidInput, result.Outcome);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Command_CreatesAdmin_AndNeverPrintsPassword()
    {
        using var factory = new AuthApiFactory();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await AdminBootstrapCommand.RunAsync(
            factory.Services,
            new[] { "admin-bootstrap", "--email", "operator@example.com" },
            readPassword: () => "super-secret",
            output,
            error);

        Assert.Equal(0, exit);
        Assert.Contains("создан", output.ToString());
        Assert.DoesNotContain("super-secret", output.ToString());
        Assert.DoesNotContain("super-secret", error.ToString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(UserRole.Admin, (await db.Users.SingleAsync()).Role);
    }

    [Fact]
    public async Task Command_Repeat_IsPredictable()
    {
        using var factory = new AuthApiFactory();
        var args = new[] { "admin-bootstrap", "--email=operator@example.com" };

        var first = await AdminBootstrapCommand.RunAsync(
            factory.Services, args, () => "secret1", new StringWriter(), new StringWriter());
        var second = await AdminBootstrapCommand.RunAsync(
            factory.Services, args, () => "secret1", new StringWriter(), new StringWriter());

        Assert.Equal(0, first);
        Assert.Equal(0, second);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public void Args_ParseEmailInBothForms()
    {
        Assert.True(AdminBootstrapArgs.IsCommand(new[] { "admin-bootstrap", "--email", "a@b.com" }));

        var spaced = AdminBootstrapArgs.Parse(new[] { "--email", "a@b.com" });
        Assert.True(spaced.IsValid);
        Assert.Equal("a@b.com", spaced.Email);

        var joined = AdminBootstrapArgs.Parse(new[] { "--email=a@b.com" });
        Assert.True(joined.IsValid);
        Assert.Equal("a@b.com", joined.Email);
    }

    [Fact]
    public void Args_RejectMalformedInput()
    {
        Assert.False(AdminBootstrapArgs.Parse(new[] { "--email" }).IsValid);
        Assert.False(AdminBootstrapArgs
            .Parse(new[] { "--email", "a@b.com", "--email", "c@d.com" }).IsValid);
        Assert.False(AdminBootstrapArgs.Parse(new[] { "--unknown" }).IsValid);
        Assert.False(AdminBootstrapArgs.Parse(Array.Empty<string>()).IsValid);
    }
}
