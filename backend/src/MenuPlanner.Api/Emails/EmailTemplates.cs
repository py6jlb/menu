using System.Net;

namespace MenuPlanner.Api.Emails;

public static class EmailTemplates
{
    public static (string Subject, string HtmlBody) Verification(string code) =>
        ("Код подтверждения почты", Layout(
            "Код подтверждения почты",
            "Для подтверждения почты введите этот код в приложении:",
            code,
            "Если вы не регистрировались в «Меню для домохозяек», просто проигнорируйте это письмо."));

    public static (string Subject, string HtmlBody) PasswordReset(string code) =>
        ("Восстановление пароля", Layout(
            "Восстановление пароля",
            "Для восстановления пароля введите этот код в приложении:",
            code,
            "Если вы не запрашивали восстановление пароля, просто проигнорируйте это письмо."));

    private static string Layout(string headline, string hint, string code, string footer)
    {
        var safeCode = WebUtility.HtmlEncode(code);
        return $@"<!DOCTYPE html>
<html lang=""ru"">
<head>
  <meta charset=""utf-8"">
  <style>
    body {{ margin: 0; padding: 24px; background: #faf6f0; font-family: Arial, sans-serif; color: #3b332b; }}
    .card {{ max-width: 480px; margin: 0 auto; background: #ffffff; border-radius: 14px; padding: 32px; }}
    .brand {{ font-weight: bold; color: #c96f2a; margin: 0 0 16px; }}
    h1 {{ font-size: 20px; margin: 0 0 12px; }}
    p {{ line-height: 1.5; margin: 0 0 16px; }}
    .code {{ font-size: 28px; letter-spacing: 8px; font-weight: bold; color: #c96f2a; }}
    .footer {{ font-size: 12px; color: #9a9083; margin-top: 24px; }}
  </style>
</head>
<body>
  <div class=""card"">
    <p class=""brand"">Меню для домохозяек</p>
    <h1>{WebUtility.HtmlEncode(headline)}</h1>
    <p>{WebUtility.HtmlEncode(hint)}</p>
    <p class=""code"">{safeCode}</p>
    <p class=""footer"">{WebUtility.HtmlEncode(footer)}</p>
  </div>
</body>
</html>";
    }
}
