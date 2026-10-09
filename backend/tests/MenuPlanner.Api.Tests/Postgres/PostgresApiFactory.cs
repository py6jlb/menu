using MenuPlanner.Api.Data;
using MenuPlanner.Api.Tests;
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

    public string DocumentsDir { get; } =
        Path.Combine(Path.GetTempPath(), "menu_planner_pgt_documents_" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("DEPLOYMENT_MODE", "lab");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PHOTOS_DIR"] = PhotosDir,
                ["DOCUMENTS_DIR"] = DocumentsDir,
                ["SMTP_HOST"] = "",
                ["SHARE_BASE_URL"] = "https://menu.example.com",
                ["DEPLOYMENT_MODE"] = "lab"
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

            ApiFactory.RemoveEmailWorker(services);

            services.AddDbContext<AppDbContext>(
                dbOptions => dbOptions.UseNpgsql(_connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var dir in new[] { PhotosDir, DocumentsDir })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
