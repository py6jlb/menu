using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MenuPlanner.Api.Tests;

/// <summary>
/// Чтение личных настроек не имеет побочных эффектов: отсутствующая строка
/// отдаётся значениями по умолчанию без записи, повторные GET не создают запись,
/// а явное сохранение — единственный путь, который пишет БД.
/// </summary>
public sealed class SettingsFlowTests
{
    [Fact]
    public async Task Get_MissingSettings_ReturnsDefaultWithoutWritingRow()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "reader");

        var (response, settings) = await GetAuthorizedAsync<UserSettingsDto>(client, user.Token, "/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SettingsCatalog.DefaultRepetitionWindowWeeks, settings!.RepetitionWindowWeeks);
        Assert.Equal(0, await CountSettingsAsync(factory, user));
    }

    [Fact]
    public async Task RepeatedGet_DoesNotCreateRow()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "reader");

        for (var i = 0; i < 3; i++)
        {
            var (response, settings) = await GetAuthorizedAsync<UserSettingsDto>(client, user.Token, "/api/settings");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(SettingsCatalog.DefaultRepetitionWindowWeeks, settings!.RepetitionWindowWeeks);
        }

        Assert.Equal(0, await CountSettingsAsync(factory, user));
    }

    [Fact]
    public async Task Put_CreatesRow_AndGetReturnsSavedValue()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "writer");

        var (putResponse, updated) = await PutAuthorizedAsync<UserSettingsDto>(client, user.Token,
            "/api/settings", new UserSettingsRequest(10));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.Equal(10, updated!.RepetitionWindowWeeks);
        Assert.Equal(1, await CountSettingsAsync(factory, user));

        var (getResponse, settings) = await GetAuthorizedAsync<UserSettingsDto>(client, user.Token, "/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(10, settings!.RepetitionWindowWeeks);
    }

    [Fact]
    public async Task Put_ExistingSettings_UpdatesWithoutSecondRow()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "writer");

        await PutAuthorizedAsync<UserSettingsDto>(client, user.Token, "/api/settings", new UserSettingsRequest(4));
        var (response, updated) = await PutAuthorizedAsync<UserSettingsDto>(client, user.Token,
            "/api/settings", new UserSettingsRequest(9));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(9, updated!.RepetitionWindowWeeks);
        Assert.Equal(1, await CountSettingsAsync(factory, user));
    }

    private static async Task<int> CountSettingsAsync(ApiFactory factory, AuthResponse user)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSettings.CountAsync(s => s.UserId == user.User.Id);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "secret1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PutAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        return (response, await ReadJsonAsync<T>(response));
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        return (response, await ReadJsonAsync<T>(response));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : System.Text.Json.JsonSerializer.Deserialize<T>(text,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
