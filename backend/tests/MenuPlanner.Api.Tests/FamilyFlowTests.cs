using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;

namespace MenuPlanner.Api.Tests;

public sealed class FamilyFlowTests
{
    [Fact]
    public async Task CreateFamily_MakesOwnerTheFirstMember_AndReturnsInviteCode()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");

        var (response, family) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token, "/api/families",
            new { name = "Семья Ивановых" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(family);
        Assert.Equal("Семья Ивановых", family.Name);
        Assert.False(string.IsNullOrWhiteSpace(family.InviteCode));
        Assert.Equal(owner.User.Id, family.OwnerId);
        Assert.Single(family.Members);
        Assert.Equal(owner.User.Id, family.Members[0].Id);
        Assert.Equal("Owner", family.Members[0].Role);
    }

    [Fact]
    public async Task Join_SecondUserByCode_ThenBothSeeTheFamily()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (joinedResponse, joined) = await PostAuthorizedAsync<FamilyDto>(client, member.Token,
            "/api/families/join", new { inviteCode = created!.InviteCode });
        Assert.Equal(HttpStatusCode.OK, joinedResponse.StatusCode);
        Assert.Equal(2, joined!.Members.Count);

        var (myResponse, my) = await GetAuthorizedAsync<FamilyDto>(client, owner.Token, "/api/families/my");
        Assert.Equal(HttpStatusCode.OK, myResponse.StatusCode);
        Assert.Equal(2, my!.Members.Count);
        Assert.Contains(my.Members, m => m.Id == owner.User.Id && m.Role == "Owner");
        Assert.Contains(my.Members, m => m.Id == member.User.Id && m.Role == "Member");
    }

    [Fact]
    public async Task Join_WhenAlreadyInFamily_ReturnsConflict()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (joinResponse, _) = await PostAuthorizedAsync<FamilyDto>(client, member.Token,
            "/api/families/join", new { inviteCode = created!.InviteCode });
        Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);

        var (duplicateResponse, _) = await PostAuthorizedAsync<FamilyDto>(client, member.Token,
            "/api/families/join", new { inviteCode = created.InviteCode });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Create_SecondFamily_ReturnsConflict()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");

        var (firstResponse, _) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var (secondResponse, _) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Другая семья" });
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Join_WithInvalidCode_ReturnsNotFound()
    {
        using var client = new ApiFactory().CreateClient();
        var member = await RegisterAsync(client, "member");

        var (response, _) = await PostAuthorizedAsync<FamilyDto>(client, member.Token,
            "/api/families/join", new { inviteCode = "NOPE1234" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RegenerateInviteCode_ByNonOwner_ReturnsForbidden()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        await PostAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/join",
            new { inviteCode = created!.InviteCode });

        var regenerate = await PostAuthorizedAsync<RegenerateResponse>(client, member.Token,
            $"/api/families/{created.Id}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);
    }

    [Fact]
    public async Task RegenerateInviteCode_ByOwner_ReturnsNewCode()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var (response, regenerated) = await PostAuthorizedAsync<RegenerateResponse>(client, owner.Token,
            $"/api/families/{created!.Id}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(regenerated);
        Assert.NotEqual(created.InviteCode, regenerated.InviteCode);
    }

    [Fact]
    public async Task RegenerateInviteCode_InvalidatesPreviousCode_AndDoesNotGrantMemberRights()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");

        var (_, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        await PostAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/join",
            new { inviteCode = created!.InviteCode });

        var (regenResponse, regenerated) = await PostAuthorizedAsync<RegenerateResponse>(client, owner.Token,
            $"/api/families/{created.Id}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.OK, regenResponse.StatusCode);

        var newcomer = await RegisterAsync(client, "newcomer");
        var (oldCode, _) = await PostAuthorizedAsync<FamilyDto>(client, newcomer.Token,
            "/api/families/join", new { inviteCode = created.InviteCode });
        Assert.Equal(HttpStatusCode.NotFound, oldCode.StatusCode);

        var (newCode, joined) = await PostAuthorizedAsync<FamilyDto>(client, newcomer.Token,
            "/api/families/join", new { inviteCode = regenerated!.InviteCode });
        Assert.Equal(HttpStatusCode.OK, newCode.StatusCode);
        Assert.Equal(3, joined!.Members.Count);

        // Смена кода не расширяет полномочия участника: управление остаётся у владельца.
        var memberRegenerate = await PostAuthorizedAsync<RegenerateResponse>(client, member.Token,
            $"/api/families/{created.Id}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.Forbidden, memberRegenerate.Response.StatusCode);

        var memberRemove = await DeleteAuthorizedAsync(client, member.Token,
            $"/api/families/{created.Id}/members/{newcomer.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, memberRemove.StatusCode);
    }

    [Fact]
    public async Task RegenerateAndRemove_ByOutsider_ReturnForbidden()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var outsider = await RegisterAsync(client, "outsider");

        var (_, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });

        var regenerate = await PostAuthorizedAsync<RegenerateResponse>(client, outsider.Token,
            $"/api/families/{created!.Id}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);

        var remove = await DeleteAuthorizedAsync(client, outsider.Token,
            $"/api/families/{created.Id}/members/{owner.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    [Fact]
    public async Task Unverified_CannotRegenerateOrRemoveMember_ReturnsForbidden()
    {
        using var factory = new ApiFactory { AutoVerifyEmailsOnRegistration = false };
        using var client = factory.CreateClient();
        var unverified = await RegisterAsync(client, "unverified");
        var familyId = Guid.NewGuid();

        var regenerate = await PostAuthorizedAsync<RegenerateResponse>(client, unverified.Token,
            $"/api/families/{familyId}/invite-code/regenerate", null);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.Response.StatusCode);

        var remove = await DeleteAuthorizedAsync(client, unverified.Token,
            $"/api/families/{familyId}/members/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ByOwner_RemovesMembership()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var (joinedResponse, _) = await PostAuthorizedAsync<FamilyDto>(client, member.Token,
            "/api/families/join", new { inviteCode = created!.InviteCode });
        Assert.Equal(HttpStatusCode.OK, joinedResponse.StatusCode);

        var removeResponse = await DeleteAuthorizedAsync(client, owner.Token,
            $"/api/families/{created.Id}/members/{member.User.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var (myResponse, _) = await GetAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/my");
        Assert.Equal(HttpStatusCode.NotFound, myResponse.StatusCode);
    }

    [Fact]
    public async Task RemoveOwner_ReturnsBadRequest()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        var removeResponse = await DeleteAuthorizedAsync(client, owner.Token,
            $"/api/families/{created!.Id}/members/{owner.User.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, removeResponse.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ByNonOwner_ReturnsForbidden()
    {
        using var client = new ApiFactory().CreateClient();
        var owner = await RegisterAsync(client, "owner");
        var member = await RegisterAsync(client, "member");
        var other = await RegisterAsync(client, "other");

        var (createdResponse, created) = await PostAuthorizedAsync<FamilyDto>(client, owner.Token,
            "/api/families", new { name = "Семья" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        await PostAuthorizedAsync<FamilyDto>(client, member.Token, "/api/families/join",
            new { inviteCode = created!.InviteCode });
        await PostAuthorizedAsync<FamilyDto>(client, other.Token, "/api/families/join",
            new { inviteCode = created.InviteCode });

        var removeResponse = await DeleteAuthorizedAsync(client, member.Token,
            $"/api/families/{created.Id}/members/{other.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
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

    private sealed record RegenerateResponse(string InviteCode);

    private static async Task<(HttpResponseMessage Response, T? Data)> PostAuthorizedAsync<T>(
        HttpClient client, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> GetAuthorizedAsync<T>(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task<HttpResponseMessage> DeleteAuthorizedAsync(
        HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
