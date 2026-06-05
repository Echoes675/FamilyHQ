namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A "this and following" split of a COUNT-bounded series was requested at a point that leaves no
/// occurrences for the forward series. The user can fix this by splitting at an earlier instance, so
/// it is a client-fixable business-rule violation; maps to HTTP 400.
/// </summary>
public sealed class InvalidSeriesSplitException : DomainValidationException
{
    public InvalidSeriesSplitException(string message)
        : base(message)
    {
    }
}
