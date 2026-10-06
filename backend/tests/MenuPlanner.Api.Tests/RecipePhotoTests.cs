using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using MenuPlanner.Api.Auth;
using MenuPlanner.Api.Families;
using MenuPlanner.Api.Recipes;

namespace MenuPlanner.Api.Tests;

public sealed class RecipePhotoTests
{
    [Fact]
    public async Task UploadPhoto_SetsPhotoUrl_StoresFileOnDisk_AndServesIt()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
        Assert.NotNull(recipe);
        Assert.Null(recipe.PhotoUrl);

        var bytes = TestImages.Png();
        var (response, dto) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe!.Id, recipe.Revision, bytes, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(dto);
        Assert.NotNull(dto.PhotoUrl);
        Assert.StartsWith("/api/photos/", dto.PhotoUrl);
        Assert.EndsWith(".png", dto.PhotoUrl);

        var storedFile = Path.Combine(factory.PhotosDir, Path.GetFileName(dto.PhotoUrl!));
        Assert.True(File.Exists(storedFile));

        var getResponse = await client.GetAsync(dto.PhotoUrl!);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/png", getResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());

        var (listResponse, list) = await GetAuthorizedAsync<List<RecipeSummaryDto>>(client, owner.Token, "/api/recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summary = Assert.Single(list!);
        Assert.Equal(dto.PhotoUrl, summary.PhotoUrl);
    }

    [Fact]
    public async Task UploadPhoto_SupportedFormats_AreStoredAndServedWithActualType()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");

        var samples = new (string Extension, byte[] Bytes, string ContentType)[]
        {
            (".png", TestImages.Png(), "image/png"),
            (".jpg", TestImages.Jpeg(), "image/jpeg"),
            (".gif", TestImages.Gif(), "image/gif"),
            (".webp", TestImages.Webp(), "image/webp")
        };

        foreach (var sample in samples)
        {
            var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());
            var (response, dto) = await PutPhotoAsync<RecipeDto>(
                client, owner.Token, recipe!.Id, recipe.Revision, sample.Bytes, sample.ContentType, $"photo{sample.Extension}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.EndsWith(sample.Extension, dto!.PhotoUrl);

            var getResponse = await client.GetAsync(dto.PhotoUrl!);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal(sample.ContentType, getResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(sample.Bytes, await getResponse.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task UploadPhoto_AnimatedGif_StoresOriginalBytesUnchanged()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        // Изображение не перекодируется: оригинал (включая анимацию) сохраняется байт-в-байт.
        var bytes = TestImages.AnimatedGif();
        var (response, dto) = await PutPhotoAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, bytes, "image/gif", "anim.gif");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".gif", dto!.PhotoUrl);
        var getResponse = await client.GetAsync(dto.PhotoUrl!);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UploadPhoto_Replacing_DeletesPreviousFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var firstBytes = TestImages.Png();
        var (_, first) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe!.Id, recipe.Revision, firstBytes, "image/png", "a.png");
        var firstFile = Path.Combine(factory.PhotosDir, Path.GetFileName(first!.PhotoUrl!));
        Assert.True(File.Exists(firstFile));

        var secondBytes = TestImages.Jpeg();
        var (response, second) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe.Id, first!.Revision, secondBytes, "image/jpeg", "b.jpg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(second!.PhotoUrl);
        Assert.NotEqual(first.PhotoUrl, second.PhotoUrl);
        Assert.False(File.Exists(firstFile));
        Assert.True(File.Exists(Path.Combine(factory.PhotosDir, Path.GetFileName(second.PhotoUrl!))));
    }

    [Fact]
    public async Task UploadPhoto_MismatchedContentType_StoresDetectedFormat()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        // Реальные байты PNG, но объявлены и названы как JPEG: формат берётся из содержимого.
        var (response, dto) = await PutPhotoAsync<RecipeDto>(
            client, owner.Token, recipe!.Id, recipe.Revision, TestImages.Png(), "image/jpeg", "photo.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".png", dto!.PhotoUrl);

        var getResponse = await client.GetAsync(dto.PhotoUrl!);
        Assert.Equal("image/png", getResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadPhoto_TextWithImageContentType_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (response, error) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            Encoding.UTF8.GetBytes("это просто текст, а не изображение"), "image/png", "fake.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(RecipeImageValidator.NotImageError, error.Error);
    }

    [Fact]
    public async Task UploadPhoto_CorruptedImage_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var valid = TestImages.Png(32, 32);
        var corrupted = valid.AsSpan(0, valid.Length / 2).ToArray();
        var (response, error) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            corrupted, "image/png", "broken.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(RecipeImageValidator.NotImageError, error.Error);
    }

    [Fact]
    public async Task UploadPhoto_ExcessiveDimensions_ReturnsBadRequest_WithoutStoringFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        // Малое по объёму изображение с огромными размерами: защита смотрит на размеры
        // из заголовка до декодирования пикселей.
        var oversized = PngHeader(5001, 5001);
        var (response, error) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            oversized, "image/png", "bomb.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(RecipeImageValidator.TooLargeDimensionsError, error.Error);
        Assert.Empty(Directory.GetFiles(factory.PhotosDir));
    }

    [Fact]
    public async Task UploadPhoto_ExcessiveSide_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        // 8001 пиксель по стороне при крошечном размере файла — превышает лимит стороны.
        var (response, error) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            PngHeader(8001, 1), "image/png", "wide.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(RecipeImageValidator.TooLargeDimensionsError, error.Error);
    }

    [Fact]
    public async Task UploadPhoto_InvalidImage_KeepsPreviousPhotoAndRevision()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (_, uploaded) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            TestImages.Png(), "image/png", "a.png");
        var storedFile = Path.Combine(factory.PhotosDir, Path.GetFileName(uploaded!.PhotoUrl!));
        Assert.True(File.Exists(storedFile));

        var (badResponse, _) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe.Id, uploaded.Revision,
            Encoding.UTF8.GetBytes("not an image"), "image/png", "bad.png");
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{recipe.Id}");
        Assert.Equal(uploaded.PhotoUrl, detail!.PhotoUrl);
        Assert.Equal(uploaded.Revision, detail.Revision);
        Assert.True(File.Exists(storedFile));
        Assert.Empty(Directory.GetFiles(factory.PhotosDir, "*.tmp"));
    }

    [Fact]
    public async Task UploadPhoto_TooLarge_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var tooLarge = new byte[5 * 1024 * 1024 + 1];
        var (response, error) = await PutPhotoAsync<RecipeErrorDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            tooLarge, "image/png", "big.png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
    }

    [Fact]
    public async Task DeletePhoto_RemovesFile_AndClearsPhotoUrl()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (_, uploaded) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            TestImages.Png(), "image/png", "a.png");
        var storedFile = Path.Combine(factory.PhotosDir, Path.GetFileName(uploaded!.PhotoUrl!));
        Assert.True(File.Exists(storedFile));

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/photo?revision={uploaded.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(storedFile));

        var (getResponse, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{recipe.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Null(detail!.PhotoUrl);

        var secondDelete = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}/photo?revision={detail.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
    }

    [Fact]
    public async Task DeletePhoto_WithoutPhoto_IsNoOp_AndDoesNotBumpRevision()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe!.Id}/photo?revision={recipe.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var (_, detail) = await GetAuthorizedAsync<RecipeDto>(client, owner.Token, $"/api/recipes/{recipe.Id}");
        // Удалять было нечего: ревизия не растёт и не делает чужие ожидаемые версии устаревшими.
        Assert.Equal(recipe.Revision, detail!.Revision);
    }

    [Fact]
    public async Task DeleteRecipe_RemovesStoredPhotoFile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "owner");
        await CreateFamilyAsync(client, owner.Token, "Семья");
        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, owner.Token, "/api/recipes", FullRequest());

        var (_, uploaded) = await PutPhotoAsync<RecipeDto>(client, owner.Token, recipe!.Id, recipe.Revision,
            TestImages.Png(), "image/png", "a.png");
        var storedFile = Path.Combine(factory.PhotosDir, Path.GetFileName(uploaded!.PhotoUrl!));
        Assert.True(File.Exists(storedFile));

        var deleteResponse = await DeleteAuthorizedAsync(
            client, owner.Token, $"/api/recipes/{recipe.Id}?revision={uploaded.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(storedFile));
    }

    [Fact]
    public async Task UploadPhoto_RecipeFromAnotherFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var first = await RegisterAsync(client, "first");
        var second = await RegisterAsync(client, "second");
        await CreateFamilyAsync(client, first.Token, "Семья первая");
        await CreateFamilyAsync(client, second.Token, "Семья вторая");

        var (_, recipe) = await PostAuthorizedAsync<RecipeDto>(client, first.Token, "/api/recipes", FullRequest());

        var (uploadResponse, _) = await PutPhotoAsync<RecipeErrorDto>(client, second.Token, recipe!.Id, recipe.Revision,
            TestImages.Png(), "image/png", "a.png");
        Assert.Equal(HttpStatusCode.NotFound, uploadResponse.StatusCode);

        var deleteResponse = await DeleteAuthorizedAsync(
            client, second.Token, $"/api/recipes/{recipe.Id}/photo?revision={recipe.Revision}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UploadPhoto_WithoutFamily_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "lonely");

        var (response, _) = await PutPhotoAsync<RecipeErrorDto>(client, user.Token, Guid.NewGuid(), 1,
            TestImages.Png(), "image/png", "a.png");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var upload = await client.PutAsync($"/api/recipes/{Guid.NewGuid()}/photo", new ByteArrayContent(Array.Empty<byte>()));
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);

        var delete = await client.DeleteAsync($"/api/recipes/{Guid.NewGuid()}/photo");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task GetPhoto_UnknownFile_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/photos/does-not-exist.png");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Валидный заголовок PNG с заданными размерами и корректным CRC, но без
    /// данных пикселей: достаточно, чтобы проверить отказ по размерам до декодирования.
    /// </summary>
    private static byte[] PngHeader(int width, int height)
    {
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var data = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4, 4), height);
        data[8] = 8;
        data[9] = 6;

        var type = Encoding.ASCII.GetBytes("IHDR");
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);

        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, type.Length);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));

        using var stream = new MemoryStream();
        stream.Write(signature);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        stream.Write(crc);
        return stream.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static async Task<(HttpResponseMessage Response, T? Data)> PutPhotoAsync<T>(
        HttpClient client, string token, Guid recipeId, int revision, byte[] bytes, string contentType, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/recipes/{recipeId}/photo?revision={revision}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        var response = await client.SendAsync(request);
        var data = await ReadJsonAsync<T>(response);
        return (response, data);
    }

    private static RecipeRequest FullRequest() => new(
        Name: "Борщ",
        Description: "Классический борщ",
        CookTimeMinutes: 90,
        Servings: 6,
        Difficulty: 3,
        Calories: 350,
        Tags: new List<string> { "суп", "первое" },
        Seasonality: new List<string> { "winter", "autumn" },
        Diet: new List<string>(),
        Steps: new List<RecipeStepRequest>
        {
            new("Сварить бульон."),
            new("Добавить свёклу.")
        },
        Ingredients: new List<RecipeIngredientRequest>
        {
            new("Свёкла", 2, "pcs", null),
            new("Соль", 0.5m, "tsp", "по вкусу")
        });

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

    private static async Task CreateFamilyAsync(HttpClient client, string token, string name)
    {
        var (response, _) = await PostAuthorizedAsync<FamilyDto>(client, token, "/api/families", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

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
