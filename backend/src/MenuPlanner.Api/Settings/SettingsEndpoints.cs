using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAuthorization();

        group.MapGet("/", GetAsync);
        group.MapPut("/", UpdateAsync);

        return app;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return Results.Unauthorized();

        var settings = await db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId.Value);
        if (settings is null)
        {
            settings = new UserSettings
            {
                UserId = userId.Value,
                RepetitionWindowWeeks = SettingsCatalog.DefaultRepetitionWindowWeeks
            };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return Results.Json(ToDto(settings));
    }

    private static async Task<IResult> UpdateAsync(
        UserSettingsRequest request,
        ClaimsPrincipal principal,
        AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return Results.Unauthorized();

        var error = Validate(request);
        if (error is not null)
            return Results.BadRequest(new SettingsErrorDto(error));

        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId.Value);
        if (settings is null)
        {
            settings = new UserSettings
            {
                UserId = userId.Value,
                RepetitionWindowWeeks = SettingsCatalog.DefaultRepetitionWindowWeeks
            };
            db.UserSettings.Add(settings);
        }

        settings.RepetitionWindowWeeks = request.RepetitionWindowWeeks!.Value;
        await db.SaveChangesAsync();

        return Results.Json(ToDto(settings));
    }

    private static string? Validate(UserSettingsRequest request)
    {
        var weeks = request.RepetitionWindowWeeks;
        if (weeks is not (>= SettingsCatalog.MinRepetitionWindowWeeks and <= SettingsCatalog.MaxRepetitionWindowWeeks))
            return $"Окно повторяемости должно быть от {SettingsCatalog.MinRepetitionWindowWeeks} до {SettingsCatalog.MaxRepetitionWindowWeeks} недель.";

        return null;
    }

    private static UserSettingsDto ToDto(UserSettings settings) =>
        new(settings.RepetitionWindowWeeks);

    private static Guid? UserIdFrom(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}