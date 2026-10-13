using MenuPlanner.Domain.Accounts;
using MenuPlanner.Domain.Families;

namespace MenuPlanner.Api.Families;

/// <summary>Тело запроса создания семьи: название.</summary>
public sealed record CreateFamilyRequest(string? Name);

/// <summary>Тело запроса вступления в семью: инвайт-код.</summary>
public sealed record JoinFamilyRequest(string? InviteCode);

/// <summary>Ошибка семейного API: текст, машинный код и путь поля.</summary>
public sealed record FamilyErrorDto(string Error, string? Code = null, string? Field = null);

/// <summary>Участник семьи: id, email и роль (Owner/Member).</summary>
public sealed record FamilyMemberDto(Guid Id, string Email, string Role);

/// <summary>Семья в API: реквизиты и список участников.</summary>
public sealed record FamilyDto(
    Guid Id,
    string Name,
    string InviteCode,
    Guid OwnerId,
    IReadOnlyList<FamilyMemberDto> Members)
{
    public static FamilyDto From(Domain.Families.Family family, IEnumerable<Domain.Families.FamilyMember> members)
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
