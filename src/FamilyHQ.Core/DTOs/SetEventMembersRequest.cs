namespace FamilyHQ.Core.DTOs;

public record SetEventMembersRequest(IReadOnlyList<Guid> MemberCalendarInfoIds);
