using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Recipes.Repetition;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class DbServicesTests
{
    [Fact]
    public async Task CurrentUserContext_ResolvesMembershipFamily_OrNull()
    {
        await using var db = NewDb();
        var owner = NewUser();
        var member = NewUser();
        var family = NewFamily(owner.Id);
        db.Users.AddRange(owner, member);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyId = family.Id,
            UserId = member.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var context = new CurrentUserContext(db);
        Assert.Equal(family.Id, await context.FamilyIdAsync(Principal(member.Id)));
        Assert.Null(await context.FamilyIdAsync(Principal(owner.Id)));
    }

    [Fact]
    public async Task SourceFamilyNameResolver_ResolvesNamesAndNull()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var resolver = new SourceFamilyNameResolver(db);
        Assert.Equal(family.Name, await resolver.ResolveAsync(family.Id));
        Assert.Null(await resolver.ResolveAsync(null));

        var many = await resolver.ResolveManyAsync(new[] { family.Id, Guid.NewGuid() });
        Assert.Equal(family.Name, Assert.Contains(family.Id, many));
    }

    [Fact]
    public async Task ExternalRecipeSourceLoader_LoadsSourcesWithContent()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        var source = NewRecipe(family.Id, "Источник");
        source.Steps.Add(new RecipeStep { Order = 0, Text = "Шаг" });
        source.Ingredients.Add(new RecipeIngredient
        {
            Order = 0,
            Name = "Вода",
            Amount = 1m,
            Unit = "l"
        });
        db.Recipes.Add(source);
        await db.SaveChangesAsync();

        var loader = new ExternalRecipeSourceLoader(db);
        var loaded = await loader.LoadSourcesAsync(new[] { source.Id, Guid.NewGuid() });

        Assert.True(loaded.ContainsKey(source.Id));
        Assert.Single(loaded[source.Id].Ingredients);
        Assert.Single(loaded[source.Id].Steps);
    }

    [Fact]
    public async Task ExternalRecipeStateResolver_ReadsShareStateFromDb()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        var source = NewRecipe(family.Id, "Источник");
        var wrapper = NewRecipe(Guid.NewGuid(), "Внешний");
        wrapper.SourceRecipeId = source.Id;
        wrapper.SourceToken = "tok";
        db.Recipes.AddRange(source, wrapper);
        db.RecipeShares.Add(new RecipeShare
        {
            Id = Guid.NewGuid(),
            RecipeId = source.Id,
            Token = "tok",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new ExternalRecipeStateResolver(db);
        var ok = await resolver.ResolveManyAsync(new[]
        {
            new ExternalSourceLink(wrapper.Id, source.Id, "tok")
        });
        Assert.Equal(ExternalRecipeState.Ok, ok[wrapper.Id]);

        var revoked = await resolver.ResolveManyAsync(new[]
        {
            new ExternalSourceLink(wrapper.Id, source.Id, "other")
        });
        Assert.Equal(ExternalRecipeState.Warning, revoked[wrapper.Id]);

        var broken = await resolver.ResolveManyAsync(new[]
        {
            new ExternalSourceLink(wrapper.Id, Guid.NewGuid(), "tok")
        });
        Assert.Equal(ExternalRecipeState.Broken, broken[wrapper.Id]);
    }

    [Fact]
    public async Task RepetitionCounter_CountsDistinctSlotsInWindow()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        var recipe = NewRecipe(family.Id, "Борщ");
        db.Recipes.Add(recipe);
        var inWindow = NewWeekPlan(family.Id, new DateOnly(2026, 1, 5));
        var secondInWindow = NewWeekPlan(family.Id, new DateOnly(2026, 1, 12));
        var outside = NewWeekPlan(family.Id, new DateOnly(2025, 12, 1));
        db.WeekPlans.AddRange(inWindow, secondInWindow, outside);
        db.PlanEntries.Add(new PlanEntry
        {
            WeekPlanId = inWindow.Id,
            Day = 0,
            MealType = MealType.Breakfast,
            RecipeId = recipe.Id,
            Portions = 2
        });
        db.PlanEntries.Add(new PlanEntry
        {
            WeekPlanId = inWindow.Id,
            Day = 3,
            MealType = MealType.Dinner,
            RecipeId = recipe.Id,
            Portions = 2
        });
        db.PlanEntries.Add(new PlanEntry
        {
            WeekPlanId = secondInWindow.Id,
            Day = 1,
            MealType = MealType.Lunch,
            RecipeId = recipe.Id,
            Portions = 2
        });
        db.PlanEntries.Add(new PlanEntry
        {
            WeekPlanId = outside.Id,
            Day = 0,
            MealType = MealType.Breakfast,
            RecipeId = recipe.Id,
            Portions = 2
        });
        await db.SaveChangesAsync();

        var counter = new RepetitionCounter(db);
        var counts = await counter.CountForFamilyAsync(
            family.Id, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 18));

        Assert.Equal(2, counts[recipe.Id]);
    }

    [Fact]
    public async Task RecipeRevisionReader_ReturnsCurrentRevision_OrZeroWhenMissing()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        var recipe = NewRecipe(family.Id, "Борщ");
        recipe.Revision = 3;
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var reader = new RecipeRevisionReader(db);
        Assert.Equal(3, await reader.CurrentAsync(recipe.Id));
        // Отсутствующий рецепт — 0, маркер «записи нет», вне диапазона реальных ревизий.
        Assert.Equal(0, await reader.CurrentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExternalRecipeNameCache_UpdatesCachedNameFromLiveSource()
    {
        await using var db = NewDb();
        var family = NewFamily(Guid.NewGuid());
        db.Families.Add(family);
        var source = NewRecipe(family.Id, "Свежее имя");
        db.Recipes.Add(source);
        var wrapper = NewRecipe(family.Id, "Устаревшее имя");
        wrapper.SourceRecipeId = source.Id;
        db.Recipes.Add(wrapper);
        await db.SaveChangesAsync();

        var cache = new ExternalRecipeNameCache(db);
        await cache.RefreshAsync(
            new[] { wrapper.Id },
            new Dictionary<Guid, Recipe> { [source.Id] = source });

        var stored = await db.Recipes.SingleAsync(r => r.Id == wrapper.Id);
        Assert.Equal("Свежее имя", stored.Name);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = Guid.NewGuid().ToString("N") + "@example.com",
        PasswordHash = "hash",
        Role = UserRole.User,
        CreatedAt = DateTime.UtcNow
    };

    private static Family NewFamily(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Семья",
        InviteCode = Guid.NewGuid().ToString("N")[..8],
        OwnerId = ownerId,
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

    private static WeekPlan NewWeekPlan(Guid familyId, DateOnly weekStart) => new()
    {
        Id = Guid.NewGuid(),
        FamilyId = familyId,
        WeekStart = weekStart,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
        }));
}
