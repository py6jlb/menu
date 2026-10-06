using System.Security.Claims;
using MenuPlanner.Api.Auth;

namespace MenuPlanner.Api.Families;

public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/families").RequireAuthorization();

        group.MapPost("/", CreateAsync).RequireVerifiedEmail();
        group.MapGet("/my", GetMyAsync);
        group.MapPost("/join", JoinAsync).RequireVerifiedEmail();
        group.MapPost("/{id:guid}/invite-code/regenerate", RegenerateInviteCodeAsync).RequireVerifiedEmail();
        group.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync).RequireVerifiedEmail();

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateFamilyRequest request,
        ClaimsPrincipal principal,
        FamilyService families)
    {
        var result = await families.CreateAsync(request.Name, principal);
        return result.Outcome == FamilyOutcome.Ok
            ? Results.Json(result.Family, statusCode: StatusCodes.Status201Created)
            : Map(result);
    }

    private static async Task<IResult> GetMyAsync(ClaimsPrincipal principal, FamilyService families)
    {
        var result = await families.GetMyAsync(principal);
        return result.Outcome == FamilyOutcome.Ok
            ? Results.Json(result.Family)
            : Map(result);
    }

    private static async Task<IResult> JoinAsync(
        JoinFamilyRequest request,
        ClaimsPrincipal principal,
        FamilyService families)
    {
        var result = await families.JoinAsync(request.InviteCode, principal);
        return result.Outcome == FamilyOutcome.Ok
            ? Results.Json(result.Family)
            : Map(result);
    }

    private static async Task<IResult> RegenerateInviteCodeAsync(
        Guid id,
        ClaimsPrincipal principal,
        FamilyService families)
    {
        var result = await families.RegenerateInviteCodeAsync(id, principal);
        return result.Outcome == FamilyOutcome.Ok
            ? Results.Json(new { inviteCode = result.InviteCode })
            : Map(result);
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid id,
        Guid userId,
        ClaimsPrincipal principal,
        FamilyService families)
    {
        var result = await families.RemoveMemberAsync(id, userId, principal);
        return result.Outcome == FamilyOutcome.Ok
            ? Results.NoContent()
            : Map(result);
    }

    private static IResult Map(FamilyAccess access) => access.Outcome switch
    {
        FamilyOutcome.Unauthorized => Results.Unauthorized(),
        FamilyOutcome.Invalid => Results.BadRequest(new FamilyErrorDto(access.Error!)),
        FamilyOutcome.Conflict => Results.Conflict(new FamilyErrorDto(access.Error!)),
        FamilyOutcome.NotFound => Results.NotFound(new FamilyErrorDto(access.Error!)),
        FamilyOutcome.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}
