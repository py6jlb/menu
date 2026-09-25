using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Auth;

public sealed class RequireVerifiedEmailFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var subject = context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return Results.Unauthorized();

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return Results.Unauthorized();

        if (!user.IsEmailVerified)
            return Results.Json(
                new ErrorDto("Подтвердите почту, чтобы вносить изменения."),
                statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}

public static class VerifiedEmailEndpointExtensions
{
    public static IEndpointConventionBuilder RequireVerifiedEmail(this IEndpointConventionBuilder builder) =>
        builder.AddEndpointFilter(new RequireVerifiedEmailFilter());
}
