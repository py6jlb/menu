using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
    public DbSet<PlanEntry> PlanEntries => Set<PlanEntry>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<AuthCode> AuthCodes => Set<AuthCode>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    public DbSet<RecipeShare> RecipeShares => Set<RecipeShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();

        user.HasIndex(u => u.Email).IsUnique();
        user.Property(u => u.Email).HasMaxLength(320);
        user.Property(u => u.PasswordHash).HasMaxLength(1024);
        user.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        user.Property(u => u.CreatedAt).HasColumnType("timestamp with time zone");

        var family = modelBuilder.Entity<Family>();

        family.HasIndex(f => f.InviteCode).IsUnique();
        family.Property(f => f.Name).HasMaxLength(200);
        family.Property(f => f.InviteCode).HasMaxLength(32);
        family.Property(f => f.CreatedAt).HasColumnType("timestamp with time zone");
        family.HasOne(f => f.Owner)
            .WithMany()
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        var member = modelBuilder.Entity<FamilyMember>();

        member.HasKey(m => new { m.FamilyId, m.UserId });
        member.Property(m => m.JoinedAt).HasColumnType("timestamp with time zone");
        member.HasOne(m => m.Family)
            .WithMany(f => f.Members)
            .HasForeignKey(m => m.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
        member.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        member.HasIndex(m => m.UserId).IsUnique();

        var recipe = modelBuilder.Entity<Recipe>();

        recipe.Property(r => r.Name).HasMaxLength(200).IsRequired();
        recipe.Property(r => r.Description).HasMaxLength(2000);
        recipe.Property(r => r.PhotoPath).HasMaxLength(500);
        recipe.Property(r => r.Calories);
        recipe.Property(r => r.CreatedAt).HasColumnType("timestamp with time zone");
        recipe.Property(r => r.UpdatedAt).HasColumnType("timestamp with time zone");
        // Токен конкурентности: UPDATE/DELETE применяются только если в БД та же
        // ревизия, что прочитана. Так устаревшее сохранение не затирает чужое.
        recipe.Property(r => r.Revision).IsConcurrencyToken().HasDefaultValue(1);
        recipe.Property(r => r.SourceToken).HasMaxLength(64);
        recipe.Property(r => r.CopiedFromFamilyName).HasMaxLength(200);
        recipe.HasIndex(r => r.FamilyId);
        // В семье не может быть двух внешних рецептов на один источник;
        // у обычных рецептов SourceRecipeId = null, поэтому индекс частичный.
        recipe.HasIndex(r => new { r.FamilyId, r.SourceRecipeId })
            .IsUnique()
            .HasFilter("\"SourceRecipeId\" IS NOT NULL");
        recipe.HasOne(r => r.Family)
            .WithMany(f => f.Recipes)
            .HasForeignKey(r => r.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        var step = modelBuilder.Entity<RecipeStep>();

        step.Property(s => s.Text).HasMaxLength(2000).IsRequired();
        step.HasIndex(s => new { s.RecipeId, s.Order });
        step.HasOne(s => s.Recipe)
            .WithMany(r => r.Steps)
            .HasForeignKey(s => s.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        var ingredient = modelBuilder.Entity<RecipeIngredient>();

        ingredient.Property(i => i.Name).HasMaxLength(200).IsRequired();
        ingredient.Property(i => i.Amount).HasPrecision(10, 2);
        ingredient.Property(i => i.Unit).HasMaxLength(32).IsRequired();
        ingredient.Property(i => i.Note).HasMaxLength(500);
        ingredient.Property(i => i.Category).HasMaxLength(RecipeCatalog.IngredientCategoryMaxLength);
        ingredient.HasIndex(i => new { i.RecipeId, i.Order });
        ingredient.HasOne(i => i.Recipe)
            .WithMany(r => r.Ingredients)
            .HasForeignKey(i => i.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        var weekPlan = modelBuilder.Entity<WeekPlan>();

        weekPlan.Property(w => w.WeekStart);
        // Ревизия — токен оптимистичной блокировки: UPDATE/DELETE получают
        // условие по прочитанной версии, поэтому устаревшая мутация не проходит.
        weekPlan.Property(w => w.Revision).IsConcurrencyToken().ValueGeneratedNever();
        weekPlan.Property(w => w.CreatedAt).HasColumnType("timestamp with time zone");
        weekPlan.Property(w => w.UpdatedAt).HasColumnType("timestamp with time zone");
        weekPlan.HasIndex(w => new { w.FamilyId, w.WeekStart }).IsUnique();
        weekPlan.HasOne(w => w.Family)
            .WithMany(f => f.WeekPlans)
            .HasForeignKey(w => w.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        var planEntry = modelBuilder.Entity<PlanEntry>();

        planEntry.Property(e => e.Day);
        planEntry.Property(e => e.MealType).HasMaxLength(16)
            .HasConversion(
                meal => PlanningCatalog.CodeOf(meal),
                code => PlanningCatalog.MealTypeFromCode(code));
        planEntry.HasIndex(e => new { e.WeekPlanId, e.Day, e.MealType }).IsUnique();
        planEntry.HasOne(e => e.WeekPlan)
            .WithMany(w => w.Entries)
            .HasForeignKey(e => e.WeekPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        planEntry.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        var userSettings = modelBuilder.Entity<UserSettings>();

        userSettings.HasKey(s => s.UserId);
        userSettings.Property(s => s.RepetitionWindowWeeks);
        userSettings.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var authCode = modelBuilder.Entity<AuthCode>();

        authCode.Property(c => c.CodeHash).HasMaxLength(1024).IsRequired();
        authCode.Property(c => c.Type).HasConversion<string>().HasMaxLength(16);
        authCode.Property(c => c.CreatedAt).HasColumnType("timestamp with time zone");
        authCode.Property(c => c.ExpiresAt).HasColumnType("timestamp with time zone");
        authCode.HasIndex(c => c.UserId);
        authCode.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var outbox = modelBuilder.Entity<EmailOutboxMessage>();

        outbox.Property(m => m.Recipient).HasMaxLength(320).IsRequired();
        outbox.Property(m => m.ProtectedPayload).HasMaxLength(2048).IsRequired();
        outbox.Property(m => m.Type).HasConversion<string>().HasMaxLength(16);
        outbox.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        outbox.Property(m => m.LastFailureReason).HasMaxLength(64);
        outbox.Property(m => m.CreatedAt).HasColumnType("timestamp with time zone");
        outbox.Property(m => m.NextAttemptAt).HasColumnType("timestamp with time zone");
        outbox.Property(m => m.ClaimedAt).HasColumnType("timestamp with time zone");
        outbox.HasIndex(m => new { m.Status, m.NextAttemptAt });
        outbox.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        outbox.HasOne(m => m.AuthCode)
            .WithMany()
            .HasForeignKey(m => m.AuthCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        var recipeShare = modelBuilder.Entity<RecipeShare>();

        recipeShare.Property(s => s.Token).HasMaxLength(64).IsRequired();
        recipeShare.Property(s => s.CreatedAt).HasColumnType("timestamp with time zone");
        recipeShare.Property(s => s.RevokedAt).HasColumnType("timestamp with time zone");
        recipeShare.HasIndex(s => s.RecipeId).IsUnique();
        recipeShare.HasIndex(s => s.Token).IsUnique();
        recipeShare.HasOne(s => s.Recipe)
            .WithMany()
            .HasForeignKey(s => s.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
