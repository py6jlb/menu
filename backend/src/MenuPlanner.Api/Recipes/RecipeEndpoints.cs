using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Photos;
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
        CurrentUserContext currentUser,
        RecipeReader recipes,
        RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeSummaryDto>());

        var counts = await repetitionCounter.CountForUserAsync(
            CurrentUser.UserId(principal), familyId.Value);
        var summaries = await recipes.ReadSummariesAsync(familyId.Value, ParseScope(scope));

        var result = summaries
            .Select(s => new RecipeSummaryDto(
                s.Id, s.Name, s.Difficulty, s.Calories, s.CookTimeMinutes, s.Servings,
                s.Tags, s.Seasonality, DietCatalog.NormalizeAll(s.Diet),
                counts.GetValueOrDefault(s.Id),
                PhotoUrl(s.PhotoPath),
                s.IsExternal,
                s.SourceFamilyName,
                s.State is { } state ? ExternalRecipeStateRules.Code(state) : null,
                s.CopiedFromFamilyName))
            .ToList();

        return Results.Json(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        RecipeReader recipes,
        RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var detail = await recipes.ReadDetailAsync(familyId.Value, id);
        if (detail is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var counts = await repetitionCounter.CountForUserAsync(
            CurrentUser.UserId(principal), familyId.Value);

        return Results.Json(ToDto(
            detail.Recipe,
            counts.GetValueOrDefault(detail.Recipe.Id),
            isExternal: detail.IsExternal,
            sourceFamilyName: detail.SourceFamilyName,
            sourceFamilyId: detail.SourceFamilyId,
            state: detail.State is { } state ? ExternalRecipeStateRules.Code(state) : null));
    }

    private static RecipeScope ParseScope(string? scope) => scope switch
    {
        "own" => RecipeScope.Own,
        "external" => RecipeScope.External,
        _ => RecipeScope.All
    };

    private static async Task<IResult> CreateAsync(
        RecipeRequest request,
        ClaimsPrincipal principal,
        CurrentUserContext currentUser,
        RecipeMutationService mutations)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        var result = await mutations.CreateAsync(familyId.Value, request);
        if (result.Outcome == RecipeMutationOutcome.ValidationError)
            return result.Validation is null
                ? Results.BadRequest(new RecipeErrorDto(result.Error ?? "Некорректные данные рецепта."))
                : RecipeErrors.Validation(result.Validation);

        return Results.Json(
            ToDto(result.Recipe!),
            statusCode: StatusCodes.Status201Created);
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
            _ when result.Validation is not null => RecipeErrors.Validation(result.Validation),
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
        CurrentUserContext currentUser,
        RecipeReader reader)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Вы не состоите в семье."));

        var error = RecipeValidation.ValidateMatch(request);
        if (error is not null)
            return RecipeErrors.Validation(error);

        // Единое актуальное чтение: живые внешние рецепты как свои, broken исключён,
        // состояние/происхождение разрешены модулем. Поиск по имени применяется до
        // ограничения выдачи — рецепт за пределами первых MaxResults тоже находится.
        var candidates = await reader.ReadMatchCandidatesAsync(familyId.Value);
        var byId = candidates.ToDictionary(c => c.Recipe.Id);

        var items = RecipeMatcher.Apply(
                candidates.Select(c => c.Recipe), request.Filters, request.Preferences, request.Search)
            .Select(m =>
            {
                var candidate = byId[m.Recipe.Id];
                return new RecipeMatchItemDto(
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
                    m.MatchScore,
                    candidate.IsExternal,
                    candidate.SourceFamilyName,
                    candidate.State is { } state ? ExternalRecipeStateRules.Code(state) : null);
            })
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

    private static IResult GetPhotoFileAsync(string fileName, IPhotoStore storage)
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
        ClaimsPrincipal principal, CurrentUserContext currentUser, RepetitionCounter repetitionCounter)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeRepetitionDto>());

        var counts = await repetitionCounter.CountForUserAsync(
            CurrentUser.UserId(principal), familyId.Value);

        var result = counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => new RecipeRepetitionDto(x.Key, x.Value))
            .ToList();

        return Results.Json(result);
    }
}
