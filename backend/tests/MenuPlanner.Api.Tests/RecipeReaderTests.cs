using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Предметное чтение рецепта: локальный id и принадлежность семьи не подменяются
/// данными источника, состояние/происхождение разрешены модулем, а live-контент
/// не появляется при сломанной ссылке.
/// </summary>
public sealed class RecipeReaderTests
{
    [Fact]
    public async Task ReadSummaries_OwnRecipe_IsNotExternal_AndKeepsStoredContent()
    {
        await using var db = NewDb();
        var family = NewFamily();
        db.Families.Add(family);
        var own = NewRecipe(family.Id, "Свой суп");
        own.Difficulty = 2;
        own.Servings = 4;
        own.SourceRecipeId = null;
        db.Recipes.Add(own);
        await db.SaveChangesAsync();

        var summaries = await Reader(db).ReadSummariesAsync(family.Id, RecipeScope.Own);

        var summary = Assert.Single(summaries);
        Assert.Equal(own.Id, summary.Id);
        Assert.Equal("Свой суп", summary.Name);
        Assert.False(summary.IsExternal);
        Assert.Null(summary.State);
        Assert.Null(summary.SourceFamilyName);
    }

    [Fact]
    public async Task ReadSummaries_ExternalWithLiveSource_UsesLiveContent_AndRefreshesCachedName()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Свежее имя");
        source.Difficulty = 5;
        source.Servings = 8;
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Устаревшее имя");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));
        await db.SaveChangesAsync();

        var summaries = await Reader(db).ReadSummariesAsync(recipientFamily.Id, RecipeScope.External);

        var summary = Assert.Single(summaries);
        Assert.Equal(wrapper.Id, summary.Id);
        Assert.Equal("Свежее имя", summary.Name);
        Assert.Equal(5, summary.Difficulty);
        Assert.Equal(8, summary.Servings);
        Assert.True(summary.IsExternal);
        Assert.Equal(ExternalRecipeState.Ok, summary.State);
        Assert.Equal(sourceFamily.Name, summary.SourceFamilyName);

        var stored = await db.Recipes.SingleAsync(r => r.Id == wrapper.Id);
        Assert.Equal("Свежее имя", stored.Name);
    }

    [Fact]
    public async Task ReadSummaries_RevokedShare_IsWarning_ButKeepsLiveContent()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Борщ");
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Борщ");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "stale";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "fresh"));
        await db.SaveChangesAsync();

        var summary = Assert.Single(await Reader(db).ReadSummariesAsync(recipientFamily.Id, RecipeScope.All));

        Assert.Equal(ExternalRecipeState.Warning, summary.State);
        Assert.Equal("Борщ", summary.Name);
    }

    [Fact]
    public async Task ReadSummaries_DeletedSource_IsBroken_AndKeepsCachedName()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var wrapper = NewRecipe(recipientFamily.Id, "Кэш имени");
        wrapper.SourceRecipeId = Guid.NewGuid();
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        await db.SaveChangesAsync();

        var summary = Assert.Single(await Reader(db).ReadSummariesAsync(recipientFamily.Id, RecipeScope.External));

        Assert.Equal("Кэш имени", summary.Name);
        Assert.True(summary.IsExternal);
        Assert.Equal(ExternalRecipeState.Broken, summary.State);
    }

    [Fact]
    public async Task ReadSummaries_DoesNotReturnForeignFamilyRecipes()
    {
        await using var db = NewDb();
        var mine = NewFamily();
        var other = NewFamily();
        db.Families.AddRange(mine, other);
        db.Recipes.Add(NewRecipe(other.Id, "Чужой"));
        db.Recipes.Add(NewRecipe(mine.Id, "Мой"));
        await db.SaveChangesAsync();

        var summaries = await Reader(db).ReadSummariesAsync(mine.Id, RecipeScope.All);

        Assert.Equal("Мой", Assert.Single(summaries).Name);
    }

    [Fact]
    public async Task ReadMatchCandidates_ExternalWithLiveSource_ResolvesContentAndMetadata()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Свежий борщ");
        source.Difficulty = 4;
        source.Ingredients.Add(new RecipeIngredient { Order = 0, Name = "Свёкла", Amount = 2m, Unit = "pcs" });
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Устаревшее имя");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));
        await db.SaveChangesAsync();

        var candidates = await Reader(db).ReadMatchCandidatesAsync(recipientFamily.Id);

        var candidate = Assert.Single(candidates);
        Assert.Equal(wrapper.Id, candidate.Recipe.Id);
        Assert.Equal("Свежий борщ", candidate.Recipe.Name);
        Assert.Equal(4, candidate.Recipe.Difficulty);
        Assert.Single(candidate.Recipe.Ingredients);
        Assert.True(candidate.IsExternal);
        Assert.Equal(ExternalRecipeState.Ok, candidate.State);
        Assert.Equal(sourceFamily.Name, candidate.SourceFamilyName);
    }

    [Fact]
    public async Task ReadMatchCandidates_BrokenSource_IsExcluded_WarningKept()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var liveSource = NewRecipe(sourceFamily.Id, "Живой");
        db.Recipes.Add(liveSource);
        var broken = NewRecipe(recipientFamily.Id, "Кэш имени");
        broken.SourceRecipeId = Guid.NewGuid();
        broken.SourceFamilyId = sourceFamily.Id;
        broken.SourceToken = "tok";
        db.Recipes.Add(broken);
        var warning = NewRecipe(recipientFamily.Id, "Отозванный");
        warning.SourceRecipeId = liveSource.Id;
        warning.SourceFamilyId = sourceFamily.Id;
        warning.SourceToken = "stale";
        db.Recipes.Add(warning);
        await db.SaveChangesAsync();

        var candidates = await Reader(db).ReadMatchCandidatesAsync(recipientFamily.Id);

        Assert.DoesNotContain(candidates, c => c.Recipe.Id == broken.Id);
        var kept = Assert.Single(candidates);
        Assert.Equal(warning.Id, kept.Recipe.Id);
        Assert.Equal(ExternalRecipeState.Warning, kept.State);
    }

    [Fact]
    public async Task ReadMatchCandidates_OwnRecipe_IsNotExternal()
    {
        await using var db = NewDb();
        var family = NewFamily();
        db.Families.Add(family);
        var own = NewRecipe(family.Id, "Свой суп");
        own.Ingredients.Add(new RecipeIngredient { Order = 0, Name = "Лук", Amount = 1m, Unit = "pcs" });
        db.Recipes.Add(own);
        await db.SaveChangesAsync();

        var candidate = Assert.Single(await Reader(db).ReadMatchCandidatesAsync(family.Id));

        Assert.False(candidate.IsExternal);
        Assert.Null(candidate.State);
        Assert.Null(candidate.SourceFamilyName);
        Assert.Single(candidate.Recipe.Ingredients);
    }

    [Fact]
    public async Task ReadDetail_ExternalWithLiveSource_KeepsLocalIdAndRevision_WithSourceContent()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Борщ");
        source.Description = "Классический";
        source.CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        source.Steps.Add(new RecipeStep { Order = 0, Text = "Сварить." });
        source.Ingredients.Add(new RecipeIngredient { Order = 0, Name = "Свёкла", Amount = 2m, Unit = "pcs" });
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Устаревшее");
        wrapper.Revision = 7;
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));
        await db.SaveChangesAsync();

        var detail = await Reader(db).ReadDetailAsync(recipientFamily.Id, wrapper.Id);

        Assert.NotNull(detail);
        Assert.Equal(wrapper.Id, detail!.Recipe.Id);
        Assert.Equal(recipientFamily.Id, detail.Recipe.FamilyId);
        Assert.Equal(7, detail.Recipe.Revision);
        Assert.Equal("Борщ", detail.Recipe.Name);
        Assert.Equal("Классический", detail.Recipe.Description);
        Assert.Equal(source.CreatedAt, detail.Recipe.CreatedAt);
        Assert.Single(detail.Recipe.Steps);
        Assert.Single(detail.Recipe.Ingredients);
        Assert.True(detail.IsExternal);
        Assert.Equal(ExternalRecipeState.Ok, detail.State);
        Assert.Equal(sourceFamily.Name, detail.SourceFamilyName);
    }

    [Fact]
    public async Task ReadDetail_RevokedShare_IsWarning_AndKeepsLiveContent()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Борщ");
        source.Steps.Add(new RecipeStep { Order = 0, Text = "Сварить." });
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Борщ");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "stale";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "fresh"));
        await db.SaveChangesAsync();

        var detail = await Reader(db).ReadDetailAsync(recipientFamily.Id, wrapper.Id);

        Assert.NotNull(detail);
        Assert.Equal(ExternalRecipeState.Warning, detail!.State);
        Assert.Equal("Борщ", detail.Recipe.Name);
        Assert.Single(detail.Recipe.Steps);
    }

    [Fact]
    public async Task ReadDetail_DeletedSource_ReturnsCachedNameWithoutInventedContent()
    {
        await using var db = NewDb();
        var recipientFamily = NewFamily();
        db.Families.Add(recipientFamily);
        var wrapper = NewRecipe(recipientFamily.Id, "Кэш имени");
        wrapper.SourceRecipeId = Guid.NewGuid();
        db.Recipes.Add(wrapper);
        await db.SaveChangesAsync();

        var detail = await Reader(db).ReadDetailAsync(recipientFamily.Id, wrapper.Id);

        Assert.NotNull(detail);
        Assert.Equal("Кэш имени", detail!.Recipe.Name);
        Assert.Empty(detail.Recipe.Steps);
        Assert.Empty(detail.Recipe.Ingredients);
        Assert.Equal(ExternalRecipeState.Broken, detail.State);
    }

    [Fact]
    public async Task ReadDetail_MissingOrForeign_ReturnsNull()
    {
        await using var db = NewDb();
        var mine = NewFamily();
        var other = NewFamily();
        db.Families.AddRange(mine, other);
        var foreign = NewRecipe(other.Id, "Чужой");
        db.Recipes.Add(foreign);
        await db.SaveChangesAsync();

        var reader = Reader(db);
        Assert.Null(await reader.ReadDetailAsync(mine.Id, Guid.NewGuid()));
        Assert.Null(await reader.ReadDetailAsync(mine.Id, foreign.Id));
    }

    [Fact]
    public async Task ReadDetail_SourceDeletedBetweenReads_TurnsBrokenAndDropsContent()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);
        var source = NewRecipe(sourceFamily.Id, "Борщ");
        source.Steps.Add(new RecipeStep { Order = 0, Text = "Сварить." });
        db.Recipes.Add(source);
        var wrapper = NewRecipe(recipientFamily.Id, "Борщ");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));
        await db.SaveChangesAsync();

        var reader = Reader(db);
        var before = await reader.ReadDetailAsync(recipientFamily.Id, wrapper.Id);
        Assert.Equal(ExternalRecipeState.Ok, before!.State);
        Assert.Single(before.Recipe.Steps);

        db.Recipes.Remove(source);
        await db.SaveChangesAsync();

        var after = await reader.ReadDetailAsync(recipientFamily.Id, wrapper.Id);
        Assert.Equal(ExternalRecipeState.Broken, after!.State);
        Assert.Empty(after.Recipe.Steps);
        Assert.Equal("Борщ", after.Recipe.Name);
    }

    [Fact]
    public async Task ReadIngredientSuggestions_CombinesOwnAndLiveExternal_RankingByFrequency()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);

        var source = NewRecipe(sourceFamily.Id, "Источник");
        source.Ingredients.Add(Ingredient("Лук"));
        source.Ingredients.Add(Ingredient("Свёкла"));
        db.Recipes.Add(source);

        var wrapper = NewRecipe(recipientFamily.Id, "Внешний");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));

        var own = NewRecipe(recipientFamily.Id, "Свой");
        own.Ingredients.Add(Ingredient("Лук"));
        own.Ingredients.Add(Ingredient("лук "));
        db.Recipes.Add(own);
        await db.SaveChangesAsync();

        var items = await Reader(db).ReadIngredientSuggestionsAsync(recipientFamily.Id, null);

        Assert.Equal("Лук", items[0]);
        Assert.Contains("Свёкла", items);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ReadIngredientSuggestions_BrokenSourceExcluded_WarningIncluded()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);

        var liveSource = NewRecipe(sourceFamily.Id, "Живой");
        liveSource.Ingredients.Add(Ingredient("Морковь"));
        db.Recipes.Add(liveSource);

        var warning = NewRecipe(recipientFamily.Id, "Отозванный");
        warning.SourceRecipeId = liveSource.Id;
        warning.SourceFamilyId = sourceFamily.Id;
        warning.SourceToken = "stale";
        db.Recipes.Add(warning);

        var broken = NewRecipe(recipientFamily.Id, "Сломанный");
        broken.SourceRecipeId = Guid.NewGuid();
        broken.SourceFamilyId = sourceFamily.Id;
        broken.SourceToken = "tok";
        broken.Ingredients.Add(Ingredient("Устаревшее"));
        db.Recipes.Add(broken);

        db.RecipeShares.Add(NewShare(liveSource.Id, "fresh"));
        await db.SaveChangesAsync();

        var items = await Reader(db).ReadIngredientSuggestionsAsync(recipientFamily.Id, null);

        Assert.Contains("Морковь", items);
        Assert.DoesNotContain("Устаревшее", items);
    }

    [Fact]
    public async Task ReadIngredientSuggestions_ReflectsSourceEditOnNextRead()
    {
        await using var db = NewDb();
        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        db.Families.AddRange(sourceFamily, recipientFamily);

        var source = NewRecipe(sourceFamily.Id, "Источник");
        var ingredient = Ingredient("Свёкла");
        source.Ingredients.Add(ingredient);
        db.Recipes.Add(source);

        var wrapper = NewRecipe(recipientFamily.Id, "Внешний");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.Add(wrapper);
        db.RecipeShares.Add(NewShare(source.Id, "tok"));
        await db.SaveChangesAsync();

        var reader = Reader(db);
        var before = await reader.ReadIngredientSuggestionsAsync(recipientFamily.Id, null);
        Assert.Contains("Свёкла", before);

        ingredient.Name = "Капуста";
        await db.SaveChangesAsync();

        var after = await reader.ReadIngredientSuggestionsAsync(recipientFamily.Id, null);
        Assert.Contains("Капуста", after);
        Assert.DoesNotContain("Свёкла", after);
    }

    [Fact]
    public async Task ReadIngredientSuggestions_NormalizesPrefixes_AndCapsAtTen()
    {
        await using var db = NewDb();
        var family = NewFamily();
        db.Families.Add(family);

        var own = NewRecipe(family.Id, "Свой");
        for (var i = 0; i < 12; i++)
            own.Ingredients.Add(Ingredient($"Продукт {i:D2}"));
        own.Ingredients.Add(Ingredient("  продукт 00  "));
        db.Recipes.Add(own);
        await db.SaveChangesAsync();

        var reader = Reader(db);
        var items = await reader.ReadIngredientSuggestionsAsync(family.Id, "  ПРОДУКТ  ");

        Assert.Equal(10, items.Count);
        Assert.Equal("Продукт 00", items[0]);
        Assert.All(items, i => Assert.StartsWith("Продукт", i, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadIngredientSuggestions_DoesNotLeakForeignFamilyIngredients()
    {
        await using var db = NewDb();
        var mine = NewFamily();
        var other = NewFamily();
        db.Families.AddRange(mine, other);

        var own = NewRecipe(mine.Id, "Мой");
        own.Ingredients.Add(Ingredient("Своё"));
        var foreign = NewRecipe(other.Id, "Чужой");
        foreign.Ingredients.Add(Ingredient("Чужое"));
        db.Recipes.AddRange(own, foreign);
        await db.SaveChangesAsync();

        var items = await Reader(db).ReadIngredientSuggestionsAsync(mine.Id, null);

        Assert.Contains("Своё", items);
        Assert.DoesNotContain("Чужое", items);
    }

    private static RecipeIngredient Ingredient(string name) => new()
    {
        Order = 0,
        Name = name,
        Amount = 1m,
        Unit = "pcs"
    };

    private static RecipeReader Reader(AppDbContext db) => new(
        db,
        new ExternalRecipeSourceLoader(db),
        new ExternalRecipeStateResolver(db),
        new ExternalRecipeNameCache(db),
        new SourceFamilyNameResolver(db));

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Family NewFamily() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Семья " + Guid.NewGuid().ToString("N")[..4],
        InviteCode = Guid.NewGuid().ToString("N")[..8],
        OwnerId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };

    private static Recipe NewRecipe(Guid familyId, string name) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        Name = name,
        CookTimeMinutes = 10,
        Servings = 2,
        Difficulty = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static RecipeShare NewShare(Guid recipeId, string token) => new()
    {
        Id = Guid.NewGuid(),
        RecipeId = recipeId,
        Token = token,
        CreatedAt = DateTime.UtcNow
    };
}
