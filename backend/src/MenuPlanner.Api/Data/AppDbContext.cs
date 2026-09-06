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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();

        user.HasIndex(u => u.Email).IsUnique();
        user.Property(u => u.Email).HasMaxLength(320);
        user.Property(u => u.PasswordHash).HasMaxLength(1024);
        user.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        user.Property(u => u.CreatedAt).HasColumnType("timestamp with time zone");
    }
}
