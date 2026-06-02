namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// An update both cleared and set a recurrence rule. The two fields are mutually exclusive by
/// contract, so the request is contradictory. Client-fixable; maps to HTTP 400.
/// </summary>
public sealed class ContradictoryRecurrenceUpdateException : DomainValidationException
{
    public ContradictoryRecurrenceUpdateException()
        : base("ClearRecurrence and RecurrenceRule are mutually exclusive: a single update cannot both remove and set a recurrence.")
    {
    }
}
