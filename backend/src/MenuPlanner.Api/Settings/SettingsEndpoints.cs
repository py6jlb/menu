using System.Security.Claims;
using MenuPlanner.Api.Auth;

namespace MenuPlanner.Api.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAuthorization();

        group.MapGet("/", GetAsync);
        group.MapPut("/", UpdateAsync).RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, SettingsService settings)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        return Results.Json(await settings.ReadAsync(userId.Value));
    }

    private static async Task<IResult> UpdateAsync(
        UserSettingsRequest request,
        ClaimsPrincipal principal,
        SettingsService settings)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var error = Validate(request);
        if (error is not null)
            return Results.BadRequest(new SettingsErrorDto(error));

        return Results.Json(await settings.SaveAsync(userId.Value, request.RepetitionWindowWeeks!.Value));
    }

    private static string? Validate(UserSettingsRequest request)
    {
        var weeks = request.RepetitionWindowWeeks;
        if (weeks is not (>= SettingsCatalog.MinRepetitionWindowWeeks and <= SettingsCatalog.MaxRepetitionWindowWeeks))
            return $"Окно повторяемости должно быть от {SettingsCatalog.MinRepetitionWindowWeeks} до {SettingsCatalog.MaxRepetitionWindowWeeks} недель.";

        return null;
    }
}
