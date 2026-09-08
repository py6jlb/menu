namespace MenuPlanner.Api.Emails;

public sealed class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "Меню для домохозяек";
    public bool EnableStartTls { get; set; } = true;
}
