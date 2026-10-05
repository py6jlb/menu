using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using MenuPlanner.Api.Data;
using Npgsql;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class DatabaseConnectionTests
{
    [Theory]
    [InlineData("plain-password")]
    [InlineData("\"abc\"")]
    [InlineData("  abc  ")]
    [InlineData("abc;Password=other")]
    [InlineData("'quote' \\ $HOME # $(command)")]
    [InlineData("  \"quote\"; \\ $ #  ")]
    public async Task LegacyComposeEnvironment_PreservesLiterals_ForResolverWithoutStructuredSupport(string literal)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "deploy/config.sh")))
            root = root.Parent;
        Assert.NotNull(root);

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var deploy = Directory.CreateDirectory(Path.Combine(directory.FullName, "deploy"));
            foreach (var filename in new[] { "config.sh", "compose.sh" })
                File.Copy(Path.Combine(root.FullName, "deploy", filename), Path.Combine(deploy.FullName, filename));
            // Публичная внешняя граница: перехватываем окружение Docker,
            // не вызывая serializer/helper напрямую и не запуская daemon.
            var start = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = directory.FullName
            };
            foreach (var argument in new[] { "-eu", "-c",
                "docker() { printf '%s' \"${ConnectionStrings__Default-}\"; }; "
                + "export -f docker; bash \"$1\" config", "test",
                Path.Combine(deploy.FullName, "compose.sh") })
                start.ArgumentList.Add(argument);
            start.Environment.Clear();
            start.Environment["PATH"] = "/usr/bin:/bin";
            start.Environment["APP_DIR"] = directory.FullName;
            start.Environment["DOCKERHUB_USER"] = "example";
            start.Environment["JWT_SECRET"] = "test-jwt";
            start.Environment["POSTGRES_DB"] = literal;
            start.Environment["POSTGRES_USER"] = literal;
            start.Environment["POSTGRES_PASSWORD"] = literal;
            start.Environment["ConnectionStrings__Default"] =
                "Host=localhost;Database=menu_planner;Username=menu;Password=menu";

            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("", error);

            // Без DB_HOST Resolve имеет ровно legacy-поведение cefd943:
            // берёт ConnectionStrings:Default, не знает о новых DB_*.
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = output }).Build();
            var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve(configuration));

            Assert.Equal("db", connection.Host);
            Assert.Equal(5432, connection.Port);
            Assert.Equal(literal, connection.Database);
            Assert.Equal(literal, connection.Username);
            Assert.Equal(literal, connection.Password);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("  abc  ")]
    [InlineData("abc;Password=other")]
    [InlineData("'quote' \\ $HOME # $(command)")]
    [InlineData("  \"quote\"; \\ $ #  ")]
    public void StructuredConfiguration_PreservesLiteralValues_WhenReparsedByNpgsql(string literal)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // Старый prod-конкатенат: Npgsql снимал кавычки с literal-пароля.
                ["ConnectionStrings:Default"] = "Host=db;Port=5432;Database=menu_planner;Username=menu;Password=\"abc\"",
                ["DB_CONNECTION_STRING"] = "Host=legacy;Password=legacy-password",
                ["DB_HOST"] = "db",
                ["DB_NAME"] = literal,
                ["DB_USER"] = literal,
                ["DB_PASSWORD"] = literal
            }).Build();

        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve(configuration));

        Assert.Equal("db", connection.Host);
        Assert.Equal(5432, connection.Port);
        Assert.Equal(literal, connection.Password);
        Assert.Equal(literal, connection.Username);
        Assert.Equal(literal, connection.Database);
    }

    [Theory]
    [InlineData("Host=explicit;Password=explicit", "Host=legacy;Password=legacy", "Host=explicit;Password=explicit")]
    [InlineData(null, "Host=legacy;Password=legacy", "Host=legacy;Password=legacy")]
    [InlineData("", "Host=legacy;Password=legacy", "")]
    [InlineData(null, "", "")]
    [InlineData(null, null, "Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu")]
    public void WithoutStructuredConfiguration_PreservesLegacyOverridePrecedence(
        string? explicitConnection, string? legacyConnection, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = explicitConnection,
                ["DB_CONNECTION_STRING"] = legacyConnection
            }).Build();

        Assert.Equal(expected, DatabaseConnection.Resolve(configuration));
    }

    [Fact]
    public void StructuredConfiguration_UsesDatabaseDefaults_WhenOptionalValuesAreUnset()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DB_HOST"] = "db" }).Build();

        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve(configuration));

        Assert.Equal("db", connection.Host);
        Assert.Equal(5432, connection.Port);
        Assert.Equal("menu_planner", connection.Database);
        Assert.Equal("menu", connection.Username);
        Assert.Equal("menu", connection.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret-invalid-port")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void StructuredConfiguration_RejectsInvalidPort_WithoutPrintingValue(string port)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DB_HOST"] = "db", ["DB_PORT"] = port }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Resolve(configuration));

        Assert.Equal("Конфигурация: DB_PORT должен быть целым от 1 до 65535", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("6543", 6543)]
    [InlineData("65535", 65535)]
    public void StructuredConfiguration_PreservesValidPort(string port, int expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DB_HOST"] = "db", ["DB_PORT"] = port }).Build();

        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve(configuration));

        Assert.Equal(expected, connection.Port);
    }

    [Fact]
    public void StructuredConfiguration_DoesNotReplaceExplicitEmptyValues_WithDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=legacy;Password=legacy",
                ["DB_HOST"] = "",
                ["DB_PORT"] = "6543",
                ["DB_NAME"] = "",
                ["DB_USER"] = "",
                ["DB_PASSWORD"] = ""
            }).Build();

        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve(configuration));

        // Npgsql нормализует пустые поля в null при сериализации/чтении;
        // они не должны превращаться в legacy-конфигурацию или defaults.
        Assert.Null(connection.Host);
        Assert.Equal(6543, connection.Port);
        Assert.Null(connection.Database);
        Assert.Null(connection.Username);
        Assert.Null(connection.Password);
    }
}
