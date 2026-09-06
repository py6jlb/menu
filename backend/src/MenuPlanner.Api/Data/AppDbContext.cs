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
        recipe.HasIndex(r => r.FamilyId);
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
        ingredient.HasIndex(i => new { i.RecipeId, i.Order });
        ingredient.HasOne(i => i.Recipe)
            .WithMany(r => r.Ingredients)
            .HasForeignKey(i => i.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
