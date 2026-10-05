namespace MenuPlanner.Api.Configuration;

/// <summary>
/// Режим запуска: <c>production</c> требует публичный HTTPS, настроенную почту и
/// непустые не-шаблонные секреты; <c>lab</c> — явный лабораторный режим (HTTP и
/// письма в лог). По умолчанию — <c>production</c>: лаборатория включается явно.
/// </summary>
public sealed class DeploymentOptions
{
    public const string ProductionMode = "production";
    public const string LabMode = "lab";

    public string Mode { get; init; } = ProductionMode;

    public bool IsProduction => Mode == ProductionMode;

    public bool IsLab => Mode == LabMode;
}
