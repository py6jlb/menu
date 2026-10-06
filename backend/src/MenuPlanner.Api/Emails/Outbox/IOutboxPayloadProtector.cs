namespace MenuPlanner.Api.Emails.Outbox;

/// <summary>
/// Обратимая защита содержимого письма в очереди. Плейнтекст кода не хранится в
/// БД и не попадает в журналы; ключ выводится из стабильного секрета
/// конфигурации, поэтому переживает перезапуск приложения.
/// </summary>
public interface IOutboxPayloadProtector
{
    string Protect(string plaintext);

    bool TryUnprotect(string protectedPayload, out string plaintext);
}
