namespace MenuPlanner.Api.Recipes;

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
