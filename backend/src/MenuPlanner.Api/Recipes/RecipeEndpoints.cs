using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Repetition;

namespace MenuPlanner.Api.Recipes;

public static class RecipeEndpoints
{
    public static IEndpointRouteBuilder MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync).RequireVerifiedEmail();
        group.MapPost("/match", MatchAsync);
        group.MapPut("/{id:guid}", UpdateAsync).RequireVerifiedEmail();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireVerifiedEmail();
        group.MapDelete("/{id:guid}/external", RemoveExternalAsync).RequireVerifiedEmail();
        group.MapPost("/{id:guid}/copy", CopyAsync).RequireVerifiedEmail();
        group.MapGet("/repetition", RepetitionAsync);
        group.MapPut("/{id:guid}/photo", UploadPhotoAsync).DisableAntiforgery().RequireVerifiedEmail();
        group.MapDelete("/{id:guid}/photo", DeletePhotoAsync).RequireVerifiedEmail();

        app.MapGet("/api/photos/{fileName}", GetPhotoFileAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? scope,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        SourceFamilyNameResolver sourceNames,
        ExternalRecipeSourceLoader sourceLoader,
        ExternalRecipeStateResolver stateResolver,
        ExternalRecipeNameCache nameCache,
        RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeSummaryDto>());

        var counts = await RepetitionCountsAsync(principal, db, repetitionCounter, familyId.Value);

        var query = db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId.Value);

        query = scope switch
        {
            "own" => query.Where(r => r.SourceRecipeId == null),
            "external" => query.Where(r => r.SourceRecipeId != null),
            _ => query
        };

        var recipes = await query
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Difficulty,
                r.Calories,
                r.CookTimeMinutes,
                r.Servings,
                r.Tags,
                r.Seasonality,
                r.Diet,
                r.PhotoPath,
                r.SourceRecipeId,
                r.SourceFamilyId,
                r.SourceToken,
                r.CopiedFromFamilyName
            })
            .ToListAsync();

        var liveSources = await sourceLoader.LoadSourcesAsync(
            recipes
                .Where(r => r.SourceRecipeId is not null)
                .Select(r => r.SourceRecipeId!.Value));

        // Кэшируется только Name внешнего рецепта и обновляется при каждом чтении.
        var staleIds = recipes
            .Where(r => r.SourceRecipeId is Guid sourceId
                && liveSources.TryGetValue(sourceId, out var live)
                && !string.Equals(r.Name, live.Name, StringComparison.Ordinal))
            .Select(r => r.Id)
            .ToList();
        await nameCache.RefreshAsync(staleIds, liveSources);

        var states = await stateResolver.ResolveManyAsync(
            recipes
                .Where(r => r.SourceRecipeId is not null)
                .Select(r => new ExternalSourceLink(r.Id, r.SourceRecipeId!.Value, r.SourceToken))
                .ToList());

        var sourceFamilyIds = recipes
            .Where(r => r.SourceFamilyId is not null)
            .Select(r => r.SourceFamilyId!.Value)
            .ToList();
        var sourceFamilyNames = await sourceNames.ResolveManyAsync(sourceFamilyIds);

        var result = recipes
            .Select(r =>
            {
                var isExternal = r.SourceRecipeId is not null;
                var state = isExternal
                    ? ExternalRecipeStateRules.Code(states[r.Id])
                    : null;
                var sourceFamilyName = isExternal && r.SourceFamilyId is Guid sourceFamilyId
                    ? sourceFamilyNames.GetValueOrDefault(sourceFamilyId)
                    : null;
                // Внешний рецепт показывает живой контент источника целиком; у него самого
                // кэшируется только Name, остальные поля могут быть пустыми/устаревшими.
                Recipe? liveSource = null;
                var hasLiveSource = isExternal
                    && liveSources.TryGetValue(r.SourceRecipeId!.Value, out liveSource);
                var name = hasLiveSource ? liveSource!.Name : r.Name;
                var difficulty = hasLiveSource ? liveSource!.Difficulty : r.Difficulty;
                var calories = hasLiveSource ? liveSource!.Calories : r.Calories;
                var cookTimeMinutes = hasLiveSource ? liveSource!.CookTimeMinutes : r.CookTimeMinutes;
                var servings = hasLiveSource ? liveSource!.Servings : r.Servings;
                var tags = hasLiveSource ? liveSource!.Tags : r.Tags;
                var seasonality = hasLiveSource ? liveSource!.Seasonality : r.Seasonality;
                var diet = DietCatalog.NormalizeAll(hasLiveSource ? liveSource!.Diet : r.Diet);
                var photoPath = hasLiveSource ? liveSource!.PhotoPath : r.PhotoPath;

                return new RecipeSummaryDto(
                    r.Id, name, difficulty, calories, cookTimeMinutes, servings, tags,
                    seasonality, diet,
                    counts.GetValueOrDefault(r.Id),
                    PhotoUrl(photoPath),
                    isExternal,
                    sourceFamilyName,
                    state,
                    r.CopiedFromFamilyName);
            })
            .ToList();

        return Results.Json(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        SourceFamilyNameResolver sourceNames,
        ExternalRecipeStateResolver stateResolver,
        ExternalRecipeNameCache nameCache,
        RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var counts = await RepetitionCountsAsync(principal, db, repetitionCounter, familyId.Value);
        var repetition = counts.GetValueOrDefault(recipe.Id);

        if (recipe.SourceRecipeId is not Guid sourceId)
            return Results.Json(ToDto(recipe, repetition));

        var source = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == sourceId);

        var sourceFamilyName = await sourceNames.ResolveAsync(recipe.SourceFamilyId);

        var state = await stateResolver.ResolveManyAsync(
            new[] { new ExternalSourceLink(recipe.Id, sourceId, recipe.SourceToken) });
        var stateCode = ExternalRecipeStateRules.Code(state[recipe.Id]);

        if (source is null)
        {
            // Сломанная ссылка: контент недоступен, остаётся только кэш имени.
            return Results.Json(ToDto(
                recipe,
                repetition,
                isExternal: true,
                sourceFamilyName: sourceFamilyName,
                sourceFamilyId: recipe.SourceFamilyId,
                state: stateCode));
        }

        // Имя кэшируется во внешнем рецепте и обновляется при каждом чтении.
        await nameCache.RefreshAsync(
            new[] { recipe.Id },
            new Dictionary<Guid, Recipe> { [sourceId] = source });

        var live = ToDto(
            source,
            repetition,
            isExternal: true,
            sourceFamilyName: sourceFamilyName,
            sourceFamilyId: recipe.SourceFamilyId,
            state: stateCode);

        return Results.Json(live with
        {
            Id = recipe.Id,
            CopiedFromFamilyName = recipe.CopiedFromFamilyName,
            // Ревизия — свойство обёртки-получателя, а не живого источника:
            // именно её передают при удалении/промоушене.
            Revision = recipe.Revision
        });
    }

    private static async Task<IResult> CreateAsync(
        RecipeRequest request, ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser)
    {
        var userId = CurrentUser.UserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        var error = RecipeValidation.Validate(request);
        if (error is not null)
            return Results.BadRequest(new RecipeErrorDto(error));

        var recipe = new Recipe
        {
            FamilyId = familyId.Value,
            Name = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        RecipeValidation.Apply(recipe, request);

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        return Results.Json(ToDto(recipe), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, RecipeRequest request, ClaimsPrincipal principal, CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var result = await mutations.UpdateAsync(new RecipeTarget(id, familyId.Value), request);
        return MutationResult(result, result.Recipe is null ? null : ToDto(result.Recipe));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, int? revision, ClaimsPrincipal principal, CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var result = await mutations.DeleteAsync(new RecipeTarget(id, familyId.Value), revision);
        return MutationResult(result);
    }

    /// <summary>
    /// Локальное удаление внешнего рецепта из своей семьи. Источник не затрагивается:
    /// обычный DELETE внешнего рецепта запрещён (403), а этот путь убирает только внешний рецепт.
    /// </summary>
    private static async Task<IResult> RemoveExternalAsync(
        Guid id, int? revision, ClaimsPrincipal principal, CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var result = await mutations.RemoveExternalAsync(new RecipeTarget(id, familyId.Value), revision);
        return MutationResult(result);
    }

    /// <summary>Отображение исхода правки/удаления в HTTP-ответ.</summary>
    private static IResult MutationResult(RecipeMutationResult result, RecipeDto? dto = null) =>
        result.Outcome switch
        {
            RecipeMutationOutcome.Ok => dto is null ? Results.NoContent() : Results.Json(dto),
            RecipeMutationOutcome.NotFound => Results.NotFound(new RecipeErrorDto("Рецепт не найден.")),
            RecipeMutationOutcome.ExternalReadOnly => RecipeErrors.ExternalReadOnly(),
            RecipeMutationOutcome.MissingRevision => result.Error is null
                ? RecipeErrors.MissingRevision()
                : Results.BadRequest(new RecipeErrorDto(result.Error)),
            RecipeMutationOutcome.Conflict => RecipeErrors.RevisionConflict(result.Revision),
            _ => Results.BadRequest(new RecipeErrorDto(result.Error ?? "Некорректные данные рецепта."))
        };

    /// <summary>
    /// Промоушен внешнего рецепта в копию на месте: внешний рецепт остаётся той же строкой Recipe
    /// (id сохраняется — записи плана не рвутся), контент источника и файл фото копируются,
    /// ссылка на источник снимается, а метка «скопировано из семьи X» сохраняется.
    /// Защита однократности и ревизии — в <see cref="ExternalRecipePromotionService"/>.
    /// </summary>
    private static async Task<IResult> CopyAsync(
        Guid id,
        int? revision,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        ExternalRecipePromotionService promotion)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var result = await promotion.PromoteAsync(new RecipeTarget(id, familyId.Value), revision);
        return result.Outcome switch
        {
            RecipePromotionOutcome.Promoted => Results.Json(ToDto(result.Recipe!)),
            RecipePromotionOutcome.NotFound => Results.NotFound(new RecipeErrorDto("Рецепт не найден.")),
            RecipePromotionOutcome.NotExternal => Results.BadRequest(new RecipeErrorDto("Это не внешний рецепт.")),
            RecipePromotionOutcome.MissingRevision => RecipeErrors.MissingRevision(),
            RecipePromotionOutcome.Conflict => RecipeErrors.RevisionConflict(result.Revision),
            _ => Results.BadRequest(new RecipeErrorDto(
                result.Error ?? "Источник удалил рецепт — копию сделать нельзя."))
        };
    }

    private static async Task<IResult> MatchAsync(
        RecipeMatchRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        ExternalRecipeSourceLoader sourceLoader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        var error = RecipeValidation.ValidateMatch(request);
        if (error is not null)
            return Results.BadRequest(new RecipeErrorDto(error));

        var recipes = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => r.FamilyId == familyId.Value)
            .ToListAsync();

        // Внешние рецепты подбираются по живому контенту источника, как свои.
        var liveSources = await sourceLoader.LoadSourcesAsync(
            recipes
                .Where(r => r.SourceRecipeId is not null)
                .Select(r => r.SourceRecipeId!.Value));
        var effective = recipes
            .Select(r => ExternalRecipeContentResolver.Resolve(r, liveSources))
            .ToList();

        var items = RecipeMatcher.Apply(effective, request.Filters, request.Preferences)
            .Select(m => new RecipeMatchItemDto(
                m.Recipe.Id,
                m.Recipe.Name,
                m.Recipe.Difficulty,
                m.Recipe.Calories,
                m.Recipe.CookTimeMinutes,
                m.Recipe.Servings,
                m.Recipe.Tags,
                m.Recipe.Seasonality,
                DietCatalog.NormalizeAll(m.Recipe.Diet),
                PhotoUrl(m.Recipe.PhotoPath),
                m.MatchScore))
            .ToList();

        return Results.Json(new RecipeMatchResponse(items));
    }

    private static async Task<IResult> UploadPhotoAsync(
        Guid id,
        IFormFile? file,
        int? revision,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        if (file is null || file.Length == 0)
            return Results.BadRequest(new RecipeErrorDto("Выберите файл изображения."));

        if (!RecipeCatalog.PhotoContentTypes.TryGetValue(file.ContentType, out var extension))
            return Results.BadRequest(new RecipeErrorDto("Файл должен быть изображением (JPEG, PNG, WebP или GIF)."));

        if (file.Length > RecipeCatalog.PhotoMaxBytes)
            return Results.BadRequest(new RecipeErrorDto(
                $"Размер фото не должен превышать {RecipeCatalog.PhotoMaxBytes / (1024 * 1024)} МБ."));

        await using var content = file.OpenReadStream();
        var result = await mutations.UploadPhotoAsync(
            new RecipeTarget(id, familyId.Value), revision, extension, content);
        return MutationResult(result, result.Recipe is null ? null : ToDto(result.Recipe));
    }

    private static async Task<IResult> DeletePhotoAsync(
        Guid id, int? revision, ClaimsPrincipal principal, CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var result = await mutations.DeletePhotoAsync(new RecipeTarget(id, familyId.Value), revision);
        return MutationResult(result);
    }

    private static IResult GetPhotoFileAsync(string fileName, PhotoStorage storage)
    {
        var path = storage.ResolveReadPath(fileName);
        if (path is null)
            return Results.NotFound();

        var contentType = ContentTypeForExtension(Path.GetExtension(path));
        if (contentType is null)
            return Results.NotFound();

        return Results.File(path, contentType);
    }

    internal static RecipeDto ToDto(
        Recipe recipe,
        int repetitionCount = 0,
        bool isExternal = false,
        string? sourceFamilyName = null,
        Guid? sourceFamilyId = null,
        string? state = null) => new(
        recipe.Id,
        recipe.Name,
        recipe.Description,
        recipe.Steps.OrderBy(s => s.Order).Select(s => s.Text).ToList(),
        recipe.CookTimeMinutes,
        recipe.Servings,
        recipe.Difficulty,
        recipe.Calories,
        recipe.Tags,
        recipe.Seasonality,
        DietCatalog.NormalizeAll(recipe.Diet),
        recipe.Ingredients.OrderBy(i => i.Order)
            .Select(i => new RecipeIngredientDto(i.Id, i.Name, i.Amount, i.Unit, i.Note))
            .ToList(),
        recipe.CreatedAt,
        recipe.UpdatedAt,
        repetitionCount,
        PhotoUrl(recipe.PhotoPath),
        isExternal,
        sourceFamilyName,
        sourceFamilyId,
        state,
        recipe.CopiedFromFamilyName,
        recipe.Revision);

    private static string? PhotoUrl(string? photoPath) =>
        photoPath is null ? null : $"/api/photos/{Path.GetFileName(photoPath)}";

    private static string? ContentTypeForExtension(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => null
    };

    private static async Task<IResult> RepetitionAsync(
        ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser, RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeRepetitionDto>());

        var counts = await RepetitionCountsAsync(principal, db, repetitionCounter, familyId.Value);

        var result = counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => new RecipeRepetitionDto(x.Key, x.Value))
            .ToList();

        return Results.Json(result);
    }

    private static async Task<Dictionary<Guid, int>> RepetitionCountsAsync(
        ClaimsPrincipal principal, AppDbContext db, RepetitionCounter repetitionCounter, Guid familyId)
    {
        var userId = CurrentUser.UserId(principal);
        var weeks = RepetitionRules.DefaultWindowWeeks;

        if (userId is { } id)
        {
            var settings = await db.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == id);
            if (settings is not null)
                weeks = settings.RepetitionWindowWeeks;
        }

        var (windowStart, windowEnd) = RepetitionRules.Window(RepetitionRules.CurrentWeekStart(), weeks);
        return await repetitionCounter.CountForFamilyAsync(familyId, windowStart, windowEnd);
    }
}
