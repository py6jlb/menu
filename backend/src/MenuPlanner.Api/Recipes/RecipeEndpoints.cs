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
        if (staleIds.Count > 0)
        {
            var stale = await db.Recipes
                .Where(r => staleIds.Contains(r.Id))
                .ToListAsync();
            foreach (var recipe in stale)
            {
                if (recipe.SourceRecipeId is Guid sourceId
                    && liveSources.TryGetValue(sourceId, out var live))
                {
                    recipe.Name = live.Name;
                }
            }

            await db.SaveChangesAsync();
        }

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
                var diet = hasLiveSource ? liveSource!.Diet : r.Diet;
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
        if (!string.Equals(recipe.Name, source.Name, StringComparison.Ordinal))
        {
            recipe.Name = source.Name;
            await db.SaveChangesAsync();
        }

        var live = ToDto(
            source,
            repetition,
            isExternal: true,
            sourceFamilyName: sourceFamilyName,
            sourceFamilyId: recipe.SourceFamilyId,
            state: stateCode);

        return Results.Json(live with { Id = recipe.Id, CopiedFromFamilyName = recipe.CopiedFromFamilyName });
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

        var error = Validate(request);
        if (error is not null)
            return Results.BadRequest(new RecipeErrorDto(error));

        var recipe = new Recipe
        {
            FamilyId = familyId.Value,
            Name = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Apply(recipe, request);

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        return Results.Json(ToDto(recipe), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, RecipeRequest request, ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser)
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
        if (recipe.SourceRecipeId is not null)
            return RecipeErrors.ExternalReadOnly();

        var error = Validate(request);
        if (error is not null)
            return Results.BadRequest(new RecipeErrorDto(error));

        recipe.UpdatedAt = DateTime.UtcNow;
        Apply(recipe, request);
        // Правка рецепта стирает метку происхождения «скопировано из семьи X».
        recipe.CopiedFromFamilyName = null;

        await db.SaveChangesAsync();

        return Results.Json(ToDto(recipe));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser, PhotoStorage storage)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));
        if (recipe.SourceRecipeId is not null)
            return RecipeErrors.ExternalReadOnly();

        var photoPath = recipe.PhotoPath;
        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
        storage.Delete(photoPath);

        return Results.NoContent();
    }

    /// <summary>
    /// Локальное удаление внешнего рецепта из своей семьи. Источник не затрагивается:
    /// обычный DELETE внешнего рецепта запрещён (403), а этот путь убирает только внешний рецепт.
    /// </summary>
    private static async Task<IResult> RemoveExternalAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));
        if (recipe.SourceRecipeId is null)
            return Results.BadRequest(new RecipeErrorDto("Это не внешний рецепт."));

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    /// <summary>
    /// Промоушен внешнего рецепта в копию на месте: внешний рецепт остаётся той же строкой Recipe
    /// (id сохраняется — записи плана не рвутся), контент источника и файл фото копируются,
    /// ссылка на источник снимается, а метка «скопировано из семьи X» сохраняется.
    /// </summary>
    private static async Task<IResult> CopyAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        SourceFamilyNameResolver sourceNames,
        PhotoStorage storage)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var wrapper = await db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (wrapper is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));
        if (wrapper.SourceRecipeId is not Guid sourceId)
            return Results.BadRequest(new RecipeErrorDto("Это не внешний рецепт."));

        var source = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Family)
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == sourceId);
        if (source is null)
        {
            // Сломанная ссылка: контента нет, спасать нечего.
            return Results.BadRequest(new RecipeErrorDto(
                "Источник удалил рецепт — копию сделать нельзя."));
        }

        // Метка «скопировано из семьи X» всегда проставляется на достижимом пути:
        // имя берём из навигации загруженного источника, с фолбэком на запись семьи.
        var copiedFromFamilyName = source.Family?.Name
            ?? await sourceNames.ResolveAsync(wrapper.SourceFamilyId)
            ?? await sourceNames.ResolveAsync(source.FamilyId);

        var copiedPhoto = source.PhotoPath is null
            ? null
            : await storage.CopyAsync(wrapper.Id, source.PhotoPath);

        wrapper.Name = source.Name;
        wrapper.Description = source.Description;
        wrapper.CookTimeMinutes = source.CookTimeMinutes;
        wrapper.Servings = source.Servings;
        wrapper.Difficulty = source.Difficulty;
        wrapper.Calories = source.Calories;
        wrapper.Tags = new List<string>(source.Tags);
        wrapper.Seasonality = new List<string>(source.Seasonality);
        wrapper.Diet = new List<string>(source.Diet);
        wrapper.PhotoPath = copiedPhoto;
        wrapper.Steps = source.Steps
            .OrderBy(s => s.Order)
            .Select(s => new RecipeStep { Order = s.Order, Text = s.Text })
            .ToList();
        wrapper.Ingredients = source.Ingredients
            .OrderBy(i => i.Order)
            .Select(i => new RecipeIngredient
            {
                Order = i.Order,
                Name = i.Name,
                Amount = i.Amount,
                Unit = i.Unit,
                Note = i.Note
            })
            .ToList();

        wrapper.SourceRecipeId = null;
        wrapper.SourceFamilyId = null;
        wrapper.SourceToken = null;
        wrapper.CopiedFromFamilyName = copiedFromFamilyName;
        wrapper.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Results.Json(ToDto(wrapper));
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

        var error = ValidateMatch(request);
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
                m.Recipe.Diet,
                PhotoUrl(m.Recipe.PhotoPath),
                m.MatchScore))
            .ToList();

        return Results.Json(new RecipeMatchResponse(items));
    }

    private static string? ValidateMatch(RecipeMatchRequest request)
    {
        var filters = request.Filters;
        if (filters is null)
            return null;

        if (filters.MaxDifficulty is { } maxDifficulty &&
            maxDifficulty is < RecipeCatalog.DifficultyMin or > RecipeCatalog.DifficultyMax)
            return $"Максимальная сложность должна быть от {RecipeCatalog.DifficultyMin} до {RecipeCatalog.DifficultyMax}.";

        if (filters.MaxCalories is { } maxCalories && maxCalories < 0)
            return "Максимальная калорийность не может быть отрицательной.";

        if (filters.MaxCookTimeMinutes is { } maxCookTime && maxCookTime < 0)
            return "Максимальное время приготовления не может быть отрицательным.";

        if (filters.Seasons is { Count: > 0 })
        {
            foreach (var season in filters.Seasons)
            {
                var normalized = season.Trim().ToLowerInvariant();
                if (!RecipeCatalog.Seasons.Contains(normalized))
                    return $"Недопустимое значение сезона: «{season}».";
            }
        }

        return null;
    }

    private static async Task<IResult> UploadPhotoAsync(
        Guid id,
        IFormFile? file,
        ClaimsPrincipal principal,
        AppDbContext db,
        CurrentUserContext currentUser,
        PhotoStorage storage)
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
        if (recipe.SourceRecipeId is not null)
            return RecipeErrors.ExternalReadOnly();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new RecipeErrorDto("Выберите файл изображения."));

        if (!RecipeCatalog.PhotoContentTypes.TryGetValue(file.ContentType, out var extension))
            return Results.BadRequest(new RecipeErrorDto("Файл должен быть изображением (JPEG, PNG, WebP или GIF)."));

        if (file.Length > RecipeCatalog.PhotoMaxBytes)
            return Results.BadRequest(new RecipeErrorDto(
                $"Размер фото не должен превышать {RecipeCatalog.PhotoMaxBytes / (1024 * 1024)} МБ."));

        var previous = recipe.PhotoPath;
        recipe.PhotoPath = await storage.SaveAsync(recipe.Id, extension, file.OpenReadStream());
        recipe.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        storage.Delete(previous);

        return Results.Json(ToDto(recipe));
    }

    private static async Task<IResult> DeletePhotoAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CurrentUserContext currentUser, PhotoStorage storage)
    {
        var familyId = await currentUser.FamilyIdAsync(principal);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));
        if (recipe.SourceRecipeId is not null)
            return RecipeErrors.ExternalReadOnly();

        var previous = recipe.PhotoPath;
        recipe.PhotoPath = null;
        recipe.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        storage.Delete(previous);

        return Results.NoContent();
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

    private static void Apply(Recipe recipe, RecipeRequest request)
    {
        recipe.Name = request.Name!.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        recipe.CookTimeMinutes = request.CookTimeMinutes!.Value;
        recipe.Servings = request.Servings!.Value;
        recipe.Difficulty = request.Difficulty!.Value;
        recipe.Calories = request.Calories;
        recipe.Tags = NormalizeStrings(request.Tags);
        recipe.Seasonality = NormalizeStrings(request.Seasonality)
            .Select(s => s.ToLowerInvariant())
            .ToList();
        recipe.Diet = NormalizeStrings(request.Diet);
        recipe.Steps = MapSteps(request);
        recipe.Ingredients = MapIngredients(request);
    }

    private static List<RecipeStep> MapSteps(RecipeRequest request)
    {
        var texts = (request.Steps ?? new List<RecipeStepRequest>())
            .Select(s => s?.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .ToList();

        return texts
            .Select((text, index) => new RecipeStep { Order = index, Text = text })
            .ToList();
    }

    private static List<RecipeIngredient> MapIngredients(RecipeRequest request)
    {
        var result = new List<RecipeIngredient>();
        var order = 0;

        foreach (var ing in request.Ingredients ?? new List<RecipeIngredientRequest>())
        {
            var name = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();

            if (string.IsNullOrEmpty(name) && ing.Amount is null && string.IsNullOrEmpty(unit) && string.IsNullOrEmpty(note))
                continue;

            result.Add(new RecipeIngredient
            {
                Order = order++,
                Name = name!,
                Amount = ing.Amount!.Value,
                Unit = unit!,
                Note = string.IsNullOrEmpty(note) ? null : note
            });
        }

        return result;
    }

    private static List<string> NormalizeStrings(List<string>? source) =>
        (source ?? new List<string>())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? Validate(RecipeRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return "Укажите название рецепта.";
        if (name.Length > RecipeCatalog.NameMaxLength)
            return $"Название рецепта не должно превышать {RecipeCatalog.NameMaxLength} символов.";

        if (request.Description?.Trim().Length > RecipeCatalog.TextMaxLength)
            return $"Описание не должно превышать {RecipeCatalog.TextMaxLength} символов.";

        if (request.CookTimeMinutes is not (>= RecipeCatalog.CookTimeMin and <= RecipeCatalog.CookTimeMax))
            return $"Укажите время приготовления (мин) от {RecipeCatalog.CookTimeMin} до {RecipeCatalog.CookTimeMax}.";

        if (request.Servings is not (>= RecipeCatalog.ServingsMin and <= RecipeCatalog.ServingsMax))
            return $"Укажите количество порций от {RecipeCatalog.ServingsMin} до {RecipeCatalog.ServingsMax}.";

        if (request.Difficulty is not (>= RecipeCatalog.DifficultyMin and <= RecipeCatalog.DifficultyMax))
            return $"Укажите сложность от {RecipeCatalog.DifficultyMin} до {RecipeCatalog.DifficultyMax}.";

        if (request.Calories is < 0 or > RecipeCatalog.CaloriesMax)
            return $"Калорийность должна быть в диапазоне от 0 до {RecipeCatalog.CaloriesMax}.";

        var steps = (request.Steps ?? new List<RecipeStepRequest>())
            .Select(s => s?.Text?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
        if (steps.Count == 0)
            return "Добавьте хотя бы один шаг приготовления.";
        if (steps.Any(s => s!.Length > RecipeCatalog.TextMaxLength))
            return $"Текст шага не должен превышать {RecipeCatalog.TextMaxLength} символов.";

        var tags = NormalizeStrings(request.Tags);
        if (tags.Any(t => t.Length > RecipeCatalog.TagMaxLength))
            return $"Теги не должны превышать {RecipeCatalog.TagMaxLength} символов.";
        if (tags.Count > 100)
            return "Слишком много тегов (максимум 100).";

        var seasons = NormalizeStrings(request.Seasonality)
            .Select(s => s.ToLowerInvariant())
            .ToList();
        var invalidSeason = seasons.FirstOrDefault(s => !RecipeCatalog.Seasons.Contains(s));
        if (invalidSeason is not null)
            return $"Недопустимое значение сезона: «{invalidSeason}».";

        var diet = NormalizeStrings(request.Diet);
        if (diet.Any(d => d.Length > RecipeCatalog.DietMaxLength))
            return $"Метки диеты не должны превышать {RecipeCatalog.DietMaxLength} символов.";

        foreach (var ing in request.Ingredients ?? new List<RecipeIngredientRequest>())
        {
            var ingredientName = ing.Name?.Trim();
            var unit = ing.Unit?.Trim();
            var note = ing.Note?.Trim();

            if (string.IsNullOrEmpty(ingredientName) && ing.Amount is null
                && string.IsNullOrEmpty(unit) && string.IsNullOrEmpty(note))
                continue;

            if (string.IsNullOrEmpty(ingredientName))
                return "Укажите название ингредиента.";
            if (ingredientName.Length > RecipeCatalog.IngredientNameMaxLength)
                return $"Название ингредиента не должно превышать {RecipeCatalog.IngredientNameMaxLength} символов.";
            if (ing.Amount is not (> 0))
                return $"Укажите количество ингредиента «{ingredientName}».";
            if (string.IsNullOrEmpty(unit))
                return $"Укажите единицу измерения ингредиента «{ingredientName}».";
            if (!RecipeCatalog.Units.Contains(unit))
                return $"Недопустимая единица измерения «{unit}».";
            if (note?.Length > RecipeCatalog.NoteMaxLength)
                return $"Примечание к ингредиенту не должно превышать {RecipeCatalog.NoteMaxLength} символов.";
        }

        return null;
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
        recipe.Diet,
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
        recipe.CopiedFromFamilyName);

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
