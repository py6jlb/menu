namespace MenuPlanner.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "menu-planner";
    public string Audience { get; set; } = "menu-planner-client";
    public string Secret { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}
