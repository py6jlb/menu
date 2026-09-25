using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

public static class RecipeShareEndpoints
{
    public static IEndpointRouteBuilder MapRecipeShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes/{id:guid}/share").RequireAuthorization();

        group.MapGet("/", GetOrCreateAsync);
        group.MapDelete("/", RevokeAsync);
        group.MapPost("/regenerate", RegenerateAsync);

        return app;
    }

    private static async Task<IResult> GetOrCreateAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ShareOptions options)
    {
        var error = await FindRecipeAsync(id, principal, db);
        if (error is not null)
            return error;

        var share = await db.RecipeShares
            .FirstOrDefaultAsync(s => s.RecipeId == id);
        if (share is null)
        {
            share = new RecipeShare
            {
                RecipeId = id,
                Token = NewToken(),
                CreatedAt = DateTime.UtcNow
            };
            db.RecipeShares.Add(share);
            await db.SaveChangesAsync();
        }

        return Results.Json(ToDto(share, options));
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ShareOptions options)
    {
        var error = await AuthorizeOwnerAsync(id, principal, db);
        if (error is not null)
            return error;

        var share = await db.RecipeShares
            .FirstOrDefaultAsync(s => s.RecipeId == id);
        if (share is null)
            return Results.NotFound(new RecipeErrorDto("Ссылка ещё не создана."));

        if (share.RevokedAt is null)
        {
            share.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return Results.Json(ToDto(share, options));
    }

    private static async Task<IResult> RegenerateAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        ShareOptions options)
    {
        var error = await AuthorizeOwnerAsync(id, principal, db);
        if (error is not null)
            return error;

        var share = await db.RecipeShares
            .FirstOrDefaultAsync(s => s.RecipeId == id);
        if (share is null)
        {
            share = new RecipeShare
            {
                RecipeId = id,
                Token = NewToken(),
                CreatedAt = DateTime.UtcNow
            };
            db.RecipeShares.Add(share);
        }
        else
        {
            share.Token = NewToken();
            share.RevokedAt = null;
        }

        await db.SaveChangesAsync();

        return Results.Json(ToDto(share, options));
    }

    private static async Task<IResult?> AuthorizeOwnerAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var access = await ResolveAccessAsync(id, principal, db);
        if (access.Error is not null)
            return access.Error;

        var family = await db.Families
            .AsNoTracking()
            .SingleAsync(f => f.Id == access.FamilyId);
        if (family.OwnerId != access.UserId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return null;
    }

    private static async Task<IResult?> FindRecipeAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db) =>
        (await ResolveAccessAsync(id, principal, db)).Error;

    /// <summary>
    /// Общая часть проверок владельца/участника: авторизация, семья, рецепт своей семьи
    /// и запрет операций над внешним рецептом. Возвращает id семьи и пользователя для
    /// дальнейшей проверки владельца.
    /// </summary>
    private static async Task<RecipeAccess> ResolveAccessAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return new RecipeAccess(Results.Unauthorized(), Guid.Empty, Guid.Empty);

        var familyId = await CurrentUser.FamilyIdAsync(principal, db);
        if (familyId is null)
            return new RecipeAccess(NotFoundRecipe(), Guid.Empty, Guid.Empty);

        var recipe = await db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return new RecipeAccess(NotFoundRecipe(), Guid.Empty, Guid.Empty);
        if (recipe.SourceRecipeId is not null)
            return new RecipeAccess(RecipeErrors.ExternalReadOnly(), Guid.Empty, Guid.Empty);

        return new RecipeAccess(null, familyId.Value, userId.Value);
    }

    private static IResult NotFoundRecipe() =>
        Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

    private sealed record RecipeAccess(IResult? Error, Guid FamilyId, Guid UserId);

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

    private static string NewToken() => Guid.NewGuid().ToString("N");

}
