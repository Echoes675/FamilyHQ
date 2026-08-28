namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// An event was submitted with no members. At least one member is required. Maps to HTTP 400.
/// </summary>
/// <remarks>
/// The API and the logs say "member"; the kiosk says "calendar", because that is the word on the
/// chips the family taps. <see cref="UserFacingMessage"/> is also the modal's own pre-save
/// validation string, so the two surfaces cannot drift apart (FHQ-175).
/// </remarks>
public sealed class NoMembersException : DomainValidationException
{
    public const string UserFacingMessage = "Please select at least one calendar.";

    public NoMembersException()
        : base("At least one member is required.", UserFacingMessage)
    {
    }
}
