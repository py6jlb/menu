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
    }
}
