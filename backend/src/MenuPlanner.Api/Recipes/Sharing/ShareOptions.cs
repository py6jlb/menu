namespace MenuPlanner.Api.Recipes.Sharing;

/// <summary>Настройки публичных ссылок: базовый URL для сборки абсолютной ссылки.</summary>
public sealed class ShareOptions
{
    public string BaseUrl { get; set; } = "";

    public string BuildUrl(string token)
    {
        var path = $"/r/{token}";
        return string.IsNullOrWhiteSpace(BaseUrl)
            ? path
            : $"{BaseUrl.TrimEnd('/')}{path}";
    }
}
