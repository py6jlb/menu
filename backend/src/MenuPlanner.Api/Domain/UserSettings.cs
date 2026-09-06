namespace MenuPlanner.Api.Domain;

public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public int RepetitionWindowWeeks { get; set; }
}