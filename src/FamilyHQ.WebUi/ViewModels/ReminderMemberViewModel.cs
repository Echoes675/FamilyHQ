namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One person a reminder ping's event is shared with, for the Reminders timeline's member chips. A
/// minimal projection — the row needs a name and a colour to draw a chip, nothing else about the
/// calendar the person came from.
/// </summary>
/// <param name="DisplayName">
/// The person's name, shown on the chip. Never collapsed to "Family" or "Shared" — the point of the
/// row is whose phone is about to go off.
/// </param>
/// <param name="Color">
/// The calendar's colour, so the chip matches the colour that event already wears everywhere else on
/// the dashboard. Null falls back to the theme accent rather than an invented colour.
/// </param>
public sealed record ReminderMemberViewModel(string DisplayName, string? Color);
