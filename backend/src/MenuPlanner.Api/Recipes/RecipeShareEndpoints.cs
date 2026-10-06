using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

public static class RecipeShareEndpoints
{
    public static IEndpointRouteBuilder MapRecipeShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes/{id:guid}/share").RequireAuthorization();

        group.MapGet("/", GetAsync);
        group.MapPost("/", CreateAsync).RequireVerifiedEmail();
        group.MapDelete("/", RevokeAsync).RequireVerifiedEmail();
        group.MapPost("/regenerate", RegenerateAsync).RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, RecipeSharingService sharing, ShareOptions options) =>
        ToResult(await sharing.GetAsync(id, principal), options);

    private static async Task<IResult> CreateAsync(
        Guid id, ClaimsPrincipal principal, RecipeSharingService sharing, ShareOptions options) =>
        ToResult(await sharing.CreateAsync(id, principal), options);

    private static async Task<IResult> RevokeAsync(
        Guid id, ClaimsPrincipal principal, RecipeSharingService sharing, ShareOptions options) =>
        ToResult(await sharing.RevokeAsync(id, principal), options);

    private static async Task<IResult> RegenerateAsync(
        Guid id, ClaimsPrincipal principal, RecipeSharingService sharing, ShareOptions options) =>
        ToResult(await sharing.RegenerateAsync(id, principal), options);

    private static IResult ToResult(RecipeShareAccess access, ShareOptions options) =>
        access.Outcome switch
        {
            RecipeShareOutcome.Ok => Results.Json(
                ToDto(access.Share!, options),
                statusCode: access.Created
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK),
            RecipeShareOutcome.Unauthorized => Results.Unauthorized(),
            RecipeShareOutcome.RecipeNotFound => Results.NotFound(new RecipeErrorDto("Рецепт не найден.")),
            RecipeShareOutcome.ExternalReadOnly => RecipeErrors.ExternalReadOnly(),
            RecipeShareOutcome.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            RecipeShareOutcome.NotCreated => Results.NotFound(new RecipeErrorDto("Ссылка ещё не создана.")),
            _ => throw new InvalidOperationException($"Неизвестный исход: {access.Outcome}.")
        };

    private static RecipeShareDto ToDto(RecipeShare share, ShareOptions options)
    {
        var path = $"/r/{share.Token}";
        return new RecipeShareDto(
            share.RecipeId,
            share.Token,
            options.BuildUrl(share.Token),
            path,
            share.CreatedAt,
            share.RevokedAt is not null,
            share.RevokedAt);
    }
}
