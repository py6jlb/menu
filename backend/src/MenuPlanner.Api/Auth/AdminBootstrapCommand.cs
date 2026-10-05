using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

/// <summary>Разобранные аргументы закрытой CLI-команды административного bootstrap.</summary>
public sealed record AdminBootstrapArgs(string? Email, string? Error)
{
    public bool IsValid => Error is null && !string.IsNullOrWhiteSpace(Email);

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] == "admin-bootstrap";

    public static AdminBootstrapArgs Parse(string[] args)
    {
        string? email = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--email")
            {
                if (i + 1 >= args.Length)
                    return new AdminBootstrapArgs(null, "после --email нужен адрес.");
                if (email is not null)
                    return new AdminBootstrapArgs(null, "email задан дважды.");
                email = args[++i];
            }
            else if (arg.StartsWith("--email=", StringComparison.Ordinal))
            {
                if (email is not null)
                    return new AdminBootstrapArgs(null, "email задан дважды.");
                email = arg["--email=".Length..];
            }
            else
            {
                return new AdminBootstrapArgs(null, $"неизвестный аргумент «{arg}».");
            }
        }

        if (string.IsNullOrWhiteSpace(email))
            return new AdminBootstrapArgs(null, "укажи --email <адрес>.");

        return new AdminBootstrapArgs(email, null);
    }
}

/// <summary>
/// Операторский вход в закрытую процедуру назначения первоначального
/// администратора. Пароль читается из stdin (скрыто на терминале, построчно при
/// перенаправлении) и нигде не логируется; email берётся из аргумента, потому
/// что не является секретом.
/// </summary>
public static class AdminBootstrapCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        string[] args,
        Func<string?>? readPassword = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken ct = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;

        var parsed = AdminBootstrapArgs.Parse(args.Length > 0 ? args[1..] : args);
        if (!parsed.IsValid)
        {
            await error.WriteLineAsync($"admin-bootstrap: {parsed.Error}");
            await error.WriteLineAsync(
                "Использование: MenuPlanner.Api admin-bootstrap --email <адрес>; пароль читается из stdin.");
            return 2;
        }

        var password = (readPassword ?? ReadPasswordHidden)();
        if (string.IsNullOrEmpty(password))
        {
            await error.WriteLineAsync("admin-bootstrap: пароль не введён.");
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(ct);

        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrap>();
        var result = await bootstrap.RunAsync(parsed.Email, password, ct);

        await output.WriteLineAsync(MessageFor(result.Outcome, parsed.Email!));
        return result.Outcome is AdminBootstrapOutcome.Created or AdminBootstrapOutcome.AlreadyInitialized
            ? 0
            : 1;
    }

    private static string MessageFor(AdminBootstrapOutcome outcome, string email) => outcome switch
    {
        AdminBootstrapOutcome.Created =>
            $"Администратор {email} создан. Почта ещё не подтверждена: введите код из письма " +
            "или запросите новый через /api/auth/verify/resend. До подтверждения административные действия недоступны.",
        AdminBootstrapOutcome.CreatedEmailFailed =>
            $"Администратор {email} создан, но письмо с кодом доставить не удалось. " +
            "Войдите и запросите код повторно (/api/auth/verify/resend).",
        AdminBootstrapOutcome.AlreadyInitialized =>
            "Первоначальный администратор уже существует. Ничего не изменено.",
        AdminBootstrapOutcome.EmailTaken =>
            $"Email {email} уже занят обычным пользователем; пароль не изменён, роль не повышена.",
        _ => "Некорректный email или слишком короткий пароль.",
    };

    private static string? ReadPasswordHidden()
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine();

        Console.Error.Write("Пароль администратора: ");
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                    buffer.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                buffer.Append(key.KeyChar);
        }
        return buffer.ToString();
    }
}
