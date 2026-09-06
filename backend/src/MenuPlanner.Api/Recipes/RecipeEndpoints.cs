using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.Repetition;

namespace MenuPlanner.Api.Recipes;

public static class RecipeEndpoints
{
    public static IEndpointRouteBuilder MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/match", MatchAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapGet("/repetition", RepetitionAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeSummaryDto>());

        var counts = await RepetitionCountsAsync(principal, db, familyId.Value);

        var recipes = await db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId.Value)
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Difficulty, r.Calories, r.CookTimeMinutes, r.Servings, r.Tags })
            .ToListAsync();

        var result = recipes
            .Select(r => new RecipeSummaryDto(
                r.Id, r.Name, r.Difficulty, r.Calories, r.CookTimeMinutes, r.Servings, r.Tags,
                counts.GetValueOrDefault(r.Id)))
            .ToList();

        return Results.Json(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var counts = await RepetitionCountsAsync(principal, db, familyId.Value);

        return Results.Json(ToDto(recipe, counts.GetValueOrDefault(recipe.Id)));
    }

    private static async Task<IResult> CreateAsync(
        RecipeRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return Results.Unauthorized();

        var familyId = await CurrentFamilyIdAsync(principal, db);
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
        Guid id, RecipeRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var error = Validate(request);
        if (error is not null)
            return Results.BadRequest(new RecipeErrorDto(error));

        recipe.UpdatedAt = DateTime.UtcNow;
        Apply(recipe, request);

        await db.SaveChangesAsync();

        return Results.Json(ToDto(recipe));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        var recipe = await db.Recipes
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId.Value);
        if (recipe is null)
            return Results.NotFound(new RecipeErrorDto("Рецепт не найден."));

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> MatchAsync(
        RecipeMatchRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
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

        var items = RecipeMatcher.Apply(recipes, request.Filters, request.Preferences)
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
                null,
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

    private static RecipeDto ToDto(Recipe recipe, int repetitionCount = 0) => new(
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
        repetitionCount);

    private static async Task<IResult> RepetitionAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var familyId = await CurrentFamilyIdAsync(principal, db);
        if (familyId is null)
            return Results.Json(Array.Empty<RecipeRepetitionDto>());

        var counts = await RepetitionCountsAsync(principal, db, familyId.Value);

        var result = counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => new RecipeRepetitionDto(x.Key, x.Value))
            .ToList();

        return Results.Json(result);
    }

    private static async Task<Dictionary<Guid, int>> RepetitionCountsAsync(
        ClaimsPrincipal principal, AppDbContext db, Guid familyId)
    {
        var userId = UserIdFrom(principal);
        var weeks = RepetitionService.DefaultWindowWeeks;

        if (userId is { } id)
        {
            var settings = await db.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == id);
            if (settings is not null)
                weeks = settings.RepetitionWindowWeeks;
        }

        var (windowStart, windowEnd) = RepetitionService.Window(RepetitionService.CurrentWeekStart(), weeks);
        return await RepetitionService.CountForFamilyAsync(db, familyId, windowStart, windowEnd);
    }

    private static async Task<Guid?> CurrentFamilyIdAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = UserIdFrom(principal);
        if (userId is null)
            return null;

        var membership = await db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId.Value);
        return membership?.FamilyId;
    }

    private static Guid? UserIdFrom(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}