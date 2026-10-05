namespace MenuPlanner.Api.Auth.Codes;

/// <summary>
/// Источник одноразовых кодов. Вынесен за шов, чтобы предметный модуль
/// подтверждения получал коды управляемо, а продакшн использовал
/// криптографически стойкую генерацию.
/// </summary>
public interface IAuthCodeGenerator
{
    string Generate();
}

public sealed class RandomAuthCodeGenerator : IAuthCodeGenerator
{
    public string Generate() => AuthCodeService.GenerateCode();
}
