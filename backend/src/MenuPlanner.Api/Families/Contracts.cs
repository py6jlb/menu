namespace MenuPlanner.Api.Families;

public sealed record CreateFamilyRequest(string? Name);

public sealed record JoinFamilyRequest(string? InviteCode);

public sealed record FamilyErrorDto(string Error);

public sealed record FamilyMemberDto(Guid Id, string Email, string Role);

public sealed record FamilyDto(
    Guid Id,
    string Name,
    string InviteCode,
    Guid OwnerId,
    IReadOnlyList<FamilyMemberDto> Members)
{
    public static FamilyDto From(Domain.Family family, IEnumerable<Domain.FamilyMember> members)
    {
        var memberDtos = members
            .Select(m => new FamilyMemberDto(
                m.UserId,
                m.User?.Email ?? "",
                m.UserId == family.OwnerId ? "Owner" : "Member"))
            .ToList();

        return new FamilyDto(family.Id, family.Name, family.InviteCode, family.OwnerId, memberDtos);
    }
}
