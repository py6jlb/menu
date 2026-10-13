using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Api.Features.Recipes;
using MenuPlanner.Api.Features.Recipes.Sharing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MenuPlanner.Domain.Families;
using MenuPlanner.Domain.Recipes;

namespace MenuPlanner.Api.Tests.Features.Recipes;

/// <summary>
/// Исходы импорта внешнего рецепта внутри модуля: недействительная и отозванная
/// ссылка, отсутствующий источник, импорт рецепта своей семьи, повторный импорт
/// и успешное создание обёртки. Проверки HTTP-статусов живут отдельно.
/// </summary>
public sealed class SharedRecipeImportServiceTests
{
    [Fact]
    public async Task Import_UnknownToken_IsInvalidLink()
    {
        await using var db = NewDb();
        var recipient = SeedFamily(db, "Семья получателя");

        var result = await Import(db, "нет-такого-токена", recipient.Id);

        Assert.Equal(SharedRecipeImportOutcome.InvalidLink, result.Outcome);
    }

    [Fact]
    public async Task Import_RevokedShare_IsInvalidLink()
    {
        await using var db = NewDb();
        var sourceFamily = SeedFamily(db, "Семья источника");
        var recipient = SeedFamily(db, "Семья получателя");
        var source = SeedRecipe(db, sourceFamily.Id, "Борщ");
        SeedShare(db, source.Id, "revoked", revoked: true);
        await db.SaveChangesAsync();

        var result = await Import(db, "revoked", recipient.Id);

        Assert.Equal(SharedRecipeImportOutcome.InvalidLink, result.Outcome);
    }

    [Fact]
    public async Task Import_DeletedSource_IsInvalidLink()
    {
        await using var db = NewDb();
        var recipient = SeedFamily(db, "Семья получателя");
        // Ссылка есть, но рецепта-источника в БД нет.
        SeedShare(db, Guid.NewGuid(), "orphan");
        await db.SaveChangesAsync();

        var result = await Import(db, "orphan", recipient.Id);

        Assert.Equal(SharedRecipeImportOutcome.InvalidLink, result.Outcome);
    }

    [Fact]
    public async Task Import_LinkToOwnFamily_IsOwnFamily()
    {
        await using var db = NewDb();
        var family = SeedFamily(db, "Семья");
        var source = SeedRecipe(db, family.Id, "Свой борщ");
        SeedShare(db, source.Id, "own");
        await db.SaveChangesAsync();

        var result = await Import(db, "own", family.Id);

        Assert.Equal(SharedRecipeImportOutcome.OwnFamily, result.Outcome);
    }

    [Fact]
    public async Task Import_AlreadyImported_ReturnsExistingWrapperId()
    {
        await using var db = NewDb();
        var sourceFamily = SeedFamily(db, "Семья источника");
        var recipient = SeedFamily(db, "Семья получателя");
        var source = SeedRecipe(db, sourceFamily.Id, "Борщ");
        SeedShare(db, source.Id, "tok");
        var wrapper = SeedRecipe(db, recipient.Id, "Борщ");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceFamilyId = sourceFamily.Id;
        wrapper.SourceToken = "tok";
        await db.SaveChangesAsync();

        var result = await Import(db, "tok", recipient.Id);

        Assert.Equal(SharedRecipeImportOutcome.AlreadyImported, result.Outcome);
        Assert.Equal(wrapper.Id, result.RecipeId);
    }

    [Fact]
    public async Task Import_CreatesExternalWrapper_WithSourceLink()
    {
        await using var db = NewDb();
        var sourceFamily = SeedFamily(db, "Семья источника");
        var recipient = SeedFamily(db, "Семья получателя");
        var source = SeedRecipe(db, sourceFamily.Id, "Борщ");
        SeedShare(db, source.Id, "tok");
        await db.SaveChangesAsync();

        var result = await Import(db, "tok", recipient.Id);

        Assert.Equal(SharedRecipeImportOutcome.Created, result.Outcome);
        var wrapper = await db.Recipes.SingleAsync(r => r.Id == result.RecipeId);
        Assert.Equal(recipient.Id, wrapper.FamilyId);
        Assert.Equal("Борщ", wrapper.Name);
        Assert.Equal(source.Id, wrapper.SourceRecipeId);
        Assert.Equal(sourceFamily.Id, wrapper.SourceFamilyId);
        Assert.Equal("tok", wrapper.SourceToken);
    }

    private static Task<SharedRecipeImportResult> Import(
        AppDbContext db, string token, Guid familyId) =>
        new SharedRecipeImportService(db, TimeProvider.System).ImportAsync(token, familyId);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Family SeedFamily(AppDbContext db, string name)
    {
        var family = new Family
        {
            Id = Guid.NewGuid(),
            Name = name,
            InviteCode = Guid.NewGuid().ToString("N")[..8],
            OwnerId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        db.Families.Add(family);
        return family;
    }

    private static Recipe SeedRecipe(AppDbContext db, Guid familyId, string name)
    {
        var recipe = new Recipe
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
        db.Recipes.Add(recipe);
        return recipe;
    }

    private static RecipeShare SeedShare(AppDbContext db, Guid recipeId, string token, bool revoked = false)
    {
        var share = new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            RevokedAt = revoked ? DateTime.UtcNow : null
        };
        db.RecipeShares.Add(share);
        return share;
    }
}
