using MenuPlanner.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MenuPlanner.Api.Tests.Postgres;

/// <summary>
/// Настоящее приложение (штатный путь старта) поверх изолированной PostgreSQL-БД:
/// применяет миграции через <c>MigrateAsync</c> при построении хоста, как в проде.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PostgresApiFactory(string connectionString) => _connectionString = connectionString;

    public string PhotosDir { get; } =
        Path.Combine(Path.GetTempPath(), "menu_planner_pgt_photos_" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PHOTOS_DIR"] = PhotosDir,
                ["SMTP_HOST"] = "",
                ["SHARE_BASE_URL"] = "https://menu.example.com"
            }));

        builder.ConfigureServices(services =>
        {
            var options = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (options is not null)
                services.Remove(options);

            var configuration = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>));
            if (configuration is not null)
                services.Remove(configuration);

            services.AddDbContext<AppDbContext>(
                dbOptions => dbOptions.UseNpgsql(_connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(PhotosDir))
        {
            try
            {
                Directory.Delete(PhotosDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
