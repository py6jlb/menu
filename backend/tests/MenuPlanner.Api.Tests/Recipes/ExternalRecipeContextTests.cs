using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests.Recipes;

/// <summary>
/// Единый module внешнего рецепта: состояние, имя семьи и контент разрешаются одним
/// правилом, а источник, исчезнувший между разрешением состояния и загрузкой контента,
/// честно даёт сломанную ссылку без устаревшего контента (ADR-0005).
/// </summary>
public sealed class ExternalRecipeContextTests
{
    [Fact]
    public async Task Resolve_SourceVanishesBetweenStateAndLoad_IsBrokenWithoutStaleContent()
    {
        // Гонка воспроизводится двумя контекстами: разрешение состояния видит живой
        // источник, а загрузка контента — уже нет. Порядок шагов внутри module тот же,
        // что и в проде, поэтому проверяется настоящее правило сломанной ссылки.
        await using var stateDb = NewDb();
        await using var contentDb = NewDb();

        var sourceFamily = NewFamily();
        var recipientFamily = NewFamily();
        stateDb.Families.AddRange(sourceFamily, recipientFamily);

        var source = NewRecipe(sourceFamily.Id, "Борщ");
        source.Steps.Add(new RecipeStep { Order = 0, Text = "Сварить бульон." });
        source.Ingredients.Add(new RecipeIngredient
        {
            Order = 0,
            Name = "Свёкла",
            Amount = 2m,
            Unit = "pcs"
        });
        stateDb.Recipes.Add(source);

        var wrapper = NewRecipe(recipientFamily.Id, "Кэш имени");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        stateDb.Recipes.Add(wrapper);
        stateDb.RecipeShares.Add(NewShare(source.Id, "tok"));
        await stateDb.SaveChangesAsync();

        // До гонки разрешение состояния считает ссылку здоровой — значит, тест именно
        // про источник, исчезнувший уже после этого шага.
        var before = await new ExternalRecipeStateResolver(stateDb).ResolveManyAsync(
            new[] { new ExternalSourceLink(wrapper.Id, source.Id, "tok") });
        Assert.Equal(ExternalRecipeState.Ok, before[wrapper.Id]);

        var context = new ExternalRecipeContext(
            new ExternalRecipeStateResolver(stateDb),
            // Пустой контекст контента: источник «удалён» между разрешением и загрузкой.
            new ExternalRecipeSourceLoader(contentDb),
            new ExternalRecipeNameCache(stateDb),
            new SourceFamilyNameResolver(stateDb));

        var resolved = await context.ResolveAsync(
            new[] { new ExternalRecipeLink(wrapper) }, ExternalRecipeProjection.Full);

        var result = resolved[wrapper.Id];
        Assert.False(result.SourceAlive);
        Assert.Equal(ExternalRecipeState.Broken, result.State);
        Assert.Equal(sourceFamily.Name, result.SourceFamilyName);

        // Устаревший контент не подставляется: остаётся только кэш имени.
        Assert.NotNull(result.Content);
        Assert.Equal("Кэш имени", result.Content!.Name);
        Assert.Empty(result.Content.Steps);
        Assert.Empty(result.Content.Ingredients);
    }

    [Fact]
    public async Task Resolve_SummaryProjection_MergesLiveMetadataAndRefreshesCachedName()
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

        var context = new ExternalRecipeContext(
            new ExternalRecipeStateResolver(db),
            new ExternalRecipeSourceLoader(db),
            new ExternalRecipeNameCache(db),
            new SourceFamilyNameResolver(db));

        var resolved = await context.ResolveAsync(
            new[] { new ExternalRecipeLink(wrapper) }, ExternalRecipeProjection.Summary);

        var result = resolved[wrapper.Id];
        Assert.True(result.SourceAlive);
        Assert.Equal(ExternalRecipeState.Ok, result.State);
        Assert.Equal(sourceFamily.Name, result.SourceFamilyName);
        Assert.Equal("Свежее имя", result.Content!.Name);
        Assert.Equal(5, result.Content.Difficulty);
        Assert.Equal(8, result.Content.Servings);

        var stored = await db.Recipes.SingleAsync(r => r.Id == wrapper.Id);
        Assert.Equal("Свежее имя", stored.Name);
    }

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
