using System.Data.Common;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes;
using MenuPlanner.Api.Recipes.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Чтение рецепта на настоящей PostgreSQL: краткий список берёт только скалярную
/// проекцию источника и не обращается к дочерним коллекциям, подробное чтение
/// материализует живой контент под локальным id.
/// </summary>
public sealed class PostgresRecipeReadTests : PostgresTestBase
{
    [PostgresFact]
    public async Task ReadSummaries_UsesScalarProjection_WithoutChildTables()
    {
        var (recipient, sources) = await SeedAsync(externalCount: 3, chunksPerSource: 5);
        var commands = new CommandRecordingInterceptor();
        await using var db = NewContext(commands);

        var summaries = await Reader(db).ReadSummariesAsync(recipient.Id, RecipeScope.All);

        Assert.Equal(4, summaries.Count);
        Assert.DoesNotContain(commands.Commands, c =>
            c.Contains("RecipeSteps", StringComparison.Ordinal)
            || c.Contains("RecipeIngredients", StringComparison.Ordinal));
        Assert.True(sources.Count == 3);
    }

    [PostgresFact]
    public async Task ReadSummaries_CommandCount_DoesNotGrowWithExternalRecipeCount()
    {
        var (recipient, _) = await SeedAsync(externalCount: 1, chunksPerSource: 1);
        var first = new CommandRecordingInterceptor();
        await using (var db = NewContext(first))
            await Reader(db).ReadSummariesAsync(recipient.Id, RecipeScope.All);

        var (recipient2, _) = await SeedAsync(externalCount: 25, chunksPerSource: 1);
        var second = new CommandRecordingInterceptor();
        await using (var db = NewContext(second))
            await Reader(db).ReadSummariesAsync(recipient2.Id, RecipeScope.All);

        Assert.Equal(first.Commands.Count, second.Commands.Count);
    }

    [PostgresFact]
    public async Task ReadDetail_MaterializesSourceContent_UnderLocalIdentity()
    {
        var (recipient, sources) = await SeedAsync(externalCount: 1, chunksPerSource: 4);
        var wrapper = await WrapperOfAsync(recipient.Id, sources[0].Id);
        var commands = new CommandRecordingInterceptor();
        await using var db = NewContext(commands);

        var detail = await Reader(db).ReadDetailAsync(recipient.Id, wrapper.Id);

        Assert.NotNull(detail);
        Assert.Equal(wrapper.Id, detail!.Recipe.Id);
        Assert.Equal(wrapper.FamilyId, detail.Recipe.FamilyId);
        Assert.True(detail.IsExternal);
        Assert.Equal(4, detail.Recipe.Steps.Count);
        Assert.Equal(4, detail.Recipe.Ingredients.Count);
    }

    private async Task<(Family Recipient, List<Recipe> Sources)> SeedAsync(
        int externalCount, int chunksPerSource)
    {
        var sourceOwner = PostgresData.NewUser($"src-{Guid.NewGuid():N}@example.com");
        var recipientOwner = PostgresData.NewUser($"dst-{Guid.NewGuid():N}@example.com");
        var sourceFamily = PostgresData.NewFamily(
            "Семья источника", $"SRC-{Guid.NewGuid().ToString("N")[..8]}", sourceOwner.Id);
        var recipient = PostgresData.NewFamily(
            "Семья получателя", $"DST-{Guid.NewGuid().ToString("N")[..8]}", recipientOwner.Id);

        await using var db = Database.CreateContext();
        db.Users.AddRange(sourceOwner, recipientOwner);
        db.Families.AddRange(sourceFamily, recipient);

        var sources = new List<Recipe>();
        var wrappers = new List<Recipe>();
        for (var i = 0; i < externalCount; i++)
        {
            var source = PostgresData.NewRecipe(sourceFamily.Id, $"Источник {i}");
            for (var c = 0; c < chunksPerSource; c++)
            {
                source.Steps.Add(new RecipeStep { Order = c, Text = $"Шаг {c}" });
                source.Ingredients.Add(new RecipeIngredient
                {
                    Order = c,
                    Name = $"Продукт {c}",
                    Amount = c + 1,
                    Unit = "g"
                });
            }
            sources.Add(source);

            var wrapper = PostgresData.NewRecipe(recipient.Id, $"Внешний {i}");
            wrapper.SourceRecipeId = source.Id;
            wrapper.SourceFamilyId = sourceFamily.Id;
            wrapper.SourceToken = $"tok-{Guid.NewGuid():N}";
            wrappers.Add(wrapper);
        }

        var own = PostgresData.NewRecipe(recipient.Id, "Свой");
        db.Recipes.AddRange(sources);
        db.Recipes.AddRange(wrappers);
        db.Recipes.Add(own);

        foreach (var source in sources)
        {
            db.RecipeShares.Add(new RecipeShare
            {
                Id = Guid.NewGuid(),
                RecipeId = source.Id,
                Token = $"tok-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (recipient, sources);
    }

    private async Task<Recipe> WrapperOfAsync(Guid familyId, Guid sourceId)
    {
        await using var db = Database.CreateContext();
        return await db.Recipes.AsNoTracking()
            .SingleAsync(r => r.FamilyId == familyId && r.SourceRecipeId == sourceId);
    }

    private AppDbContext NewContext(CommandRecordingInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.ConnectionString);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options);
    }

    private static RecipeReader Reader(AppDbContext db) => new(
        db,
        new ExternalRecipeSourceLoader(db),
        new ExternalRecipeStateResolver(db),
        new ExternalRecipeNameCache(db),
        new SourceFamilyNameResolver(db));

    internal sealed class CommandRecordingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return new(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return new(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return new(result);
        }
    }
}
