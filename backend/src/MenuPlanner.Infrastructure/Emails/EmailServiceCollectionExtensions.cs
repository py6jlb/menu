using MenuPlanner.Infrastructure.Configuration;
using MenuPlanner.Infrastructure.Emails.Contracts;
using MenuPlanner.Infrastructure.Emails.Options;
using MenuPlanner.Infrastructure.Emails.Outbox;
using MenuPlanner.Infrastructure.Emails.Services;

namespace MenuPlanner.Infrastructure.Emails;

/// <summary>
/// Регистрация отправки писем: транспорт, отправитель и надёжная очередь.
/// Ключ защиты содержимого очереди выводится из <c>EMAIL_OUTBOX_KEY</c> или
/// секрета JWT, который модуль читает сам, — порядок вызова с
/// <c>AddJwtAuthentication</c> не важен.
/// </summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmailDelivery(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ProductionConfiguration уже отклонил production+log на старте; здесь транспорт
        // выбирается по явному режиму, без автоматического падения в логирование.
        var emailOptions = EmailConfiguration.Read(configuration);
        services.AddSingleton(emailOptions);
        services.AddSingleton<IEmailTransport>(provider =>
            emailOptions.Transport == EmailTransportMode.Log
                ? new LoggingEmailTransport(provider.GetRequiredService<ILogger<LoggingEmailTransport>>())
                : new SmtpEmailTransport(emailOptions));
        services.AddSingleton<EmailSender>();

        // Надёжная очередь писем: принятая доставка живёт в PostgreSQL, содержимое
        // зашифровано ключом из конфигурации, отправка — фоновая с ограниченными повторами.
        var outboxOptions = EmailOutboxOptions.Read(configuration);
        services.AddSingleton(outboxOptions);
        services.AddSingleton<IOutboxPayloadProtector>(
            new AesGcmOutboxPayloadProtector(
                OutboxProtectionKey.Derive(configuration, JwtOptions.Read(configuration).Secret)));
        services.AddScoped<EmailOutbox>();
        services.AddScoped<EmailOutboxProcessor>();
        services.AddSingleton<EmailDispatchTrigger>();
        services.AddHostedService<EmailDeliveryWorker>();
        return services;
    }
}
