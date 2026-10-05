using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "menu_planner_test_" + Guid.NewGuid().ToString("N");
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    public string PhotosDir { get; } =
        Path.Combine(Path.GetTempPath(), "menu_planner_photos_" + Guid.NewGuid().ToString("N"));

    public bool AutoVerifyEmailsOnRegistration { get; set; } = true;

    /// <summary>Настройки конкретного теста поверх лабораторных defaults.</summary>
    public Dictionary<string, string?> Settings { get; } = new();

    /// <summary>Имя in-memory базы — общее для дополнительных контекстов в тестах.</summary>
    public string DatabaseName => _databaseName;

    /// <summary>Общий корень in-memory базы: делится между контекстами с тем же именем.</summary>
    public InMemoryDatabaseRoot DatabaseRoot => _databaseRoot;

    /// <summary>Дополнительные interceptor'ы, подключаемые к <see cref="AppDbContext"/>.</summary>
    public List<IInterceptor> Interceptors { get; } = new();

    public async Task VerifyUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // DEPLOYMENT_MODE читается на старте (до применения in-memory defaults),
        // поэтому лабораторный режим задаётся через UseSetting.
        builder.UseSetting("DEPLOYMENT_MODE", "lab");
        foreach (var pair in Settings)
            if (pair.Value is not null)
                builder.UseSetting(pair.Key, pair.Value);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Тесты по умолчанию — явный лабораторный режим (HTTP и письма в лог).
            // Проверки production переопределяют DEPLOYMENT_MODE через Settings.
            var settings = new Dictionary<string, string?>
            {
                ["PHOTOS_DIR"] = PhotosDir,
                ["SMTP_HOST"] = "",
                ["SHARE_BASE_URL"] = "https://menu.example.com",
                ["DEPLOYMENT_MODE"] = "lab"
            };
            foreach (var pair in Settings) settings[pair.Key] = pair.Value;
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            var configDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>));
            if (configDescriptor is not null)
                services.Remove(configDescriptor);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName, _databaseRoot)
                    .AddInterceptors(new AutoVerifyEmailsInterceptor(this));
                foreach (var interceptor in Interceptors)
                    options.AddInterceptors(interceptor);
            });
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

    private sealed class AutoVerifyEmailsInterceptor : SaveChangesInterceptor
    {
        private readonly ApiFactory _factory;

        public AutoVerifyEmailsInterceptor(ApiFactory factory) => _factory = factory;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            MarkVerified(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            MarkVerified(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void MarkVerified(DbContextEventData eventData)
        {
            if (!_factory.AutoVerifyEmailsOnRegistration || eventData.Context is null)
                return;

            foreach (var entry in eventData.Context.ChangeTracker.Entries<User>())
            {
                if (entry.State != EntityState.Added)
                    continue;

                entry.Entity.IsEmailVerified = true;
                entry.Entity.EmailVerifiedAt = DateTime.UtcNow;
            }
        }
    }
}
