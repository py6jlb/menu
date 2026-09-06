using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration["DB_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.MapGet("/health", () => Results.Json(
    new { status = "ok", service = "menu-planner-api" }));

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();
