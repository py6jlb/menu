using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MenuPlanner.Api.Auth;

/// <summary>
/// Чистые хелперы по вызывающему: кто он по claim. Членство в семье (чтение БД)
/// вынесено в scoped <see cref="CurrentUserContext"/>.
/// </summary>
public static class CurrentUser
{
    /// <summary>Id текущего пользователя из claim <c>sub</c> или null, если claim нет/некорректен.</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
