using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure, testable state for the <c>ReminderPicker</c> component: the three reminder states, the
/// all-day offset arithmetic, and the limits the picker has to make unreachable. No Razor, no DI —
/// the render and the interaction are covered separately by E2E, the same split
/// <see cref="RecurrencePickerModel"/> uses.
/// </summary>
/// <remarks>
/// <para>
/// Two things about Google shape everything here. First, it accepts nearly any reminder with a
/// <c>200</c> and then rewrites it silently: a negative offset becomes <c>0</c>, anything past the
/// ceiling is clamped to it, duplicates are collapsed, and a method it does not recognise is dropped
/// outright — leaving an event with no reminder while the request looked successful. Only a sixth
/// override is actually rejected. So the picker has to refuse those values itself; there is no
/// server-side error to fall back on.
/// </para>
/// <para>
/// Second, values Google <i>supplied</i> are never validated or normalised on the way in. An offset
/// or a method the kiosk could not have created can still arrive from a phone, and Google is the
/// authority on its own data — so the read path keeps it verbatim and the tab displays it.
/// </para>
/// <para>
/// <see cref="HasChanged"/> is the single gate on whether the save path sends reminders at all. While
/// it is false the request carries no reminders key and Google's copy is untouched, which is what
/// stops an ordinary title edit from rewriting what somebody set on a phone.
/// </para>
/// <para>
/// The add form is the one piece of state a save reads rather than ignores:
/// <see cref="CommitPendingFormReminder"/> commits the reminder it describes when — and only when —
/// the family configured one and never pressed Add. <see cref="IsFormTouched"/> is what makes that
/// safe, because the form holds a value before anybody touches it.
/// </para>
/// </remarks>
public sealed class ReminderPickerModel
{
    /// <summary>Minutes in a day — the unit an all-day reminder's offset is measured in.</summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// The nearest an all-day reminder can be set: the day before. Anything nearer would need a
    /// non-positive offset from the day's midnight, which Google clamps to <c>0</c> — a notification
    /// at midnight rather than the time the family chose.
    /// </summary>
    public const int MinimumDaysBefore = 1;

    /// <summary>
    /// The furthest an all-day reminder can be set, derived from the ceiling Google stores so the two
    /// cannot drift apart. An offset at the ceiling is midnight this many days before.
    /// </summary>
    public const int MaximumDaysBefore = EventRemindersValidator.MaxMinutes / MinutesPerDay;

    // The form's starting position for an all-day reminder's time of day. It is NOT a claim about
    // Google's own all-day default, which the API does not expose: the family sees this value and
    // commits it deliberately with Add, or changes it first.
    private static readonly TimeOnly DefaultTimeOfDay = new(9, 0);

    // The form's starting position for a timed reminder. Same reasoning as DefaultTimeOfDay.
    private const int DefaultAmount = 30;

    // The reminders the tab opened with, normalised so a never-synced event compares equal to one
    // that follows the calendar. HasChanged is measured against this and nothing else.
    private readonly EventReminders _opened;
    private readonly List<EventReminder> _overrides = [];
    private int _daysBefore = MinimumDaysBefore;
    private int _amount = DefaultAmount;
    private ReminderUnit _unit = ReminderUnit.Minutes;
    private TimeOnly _timeOfDay = DefaultTimeOfDay;
    private string _method = EventRemindersValidator.PopupMethod;
    private bool _isFormTouched;

    // Reminders the family took away with Remove during this visit. Kept so the save cannot hand one
    // of them back — see CommitPendingFormReminder.
    private readonly HashSet<EventReminder> _withdrawn = [];

    private ReminderPickerModel(EventReminders opened, IReadOnlyList<EventReminder>? calendarDefault, bool isAllDay)
    {
        _opened = opened;
        CalendarDefault = calendarDefault;
        IsAllDay = isAllDay;
        FollowsCalendarDefault = opened.UseDefault;
        _overrides.AddRange(InDisplayOrder(opened.Overrides));
    }

    /// <summary>
    /// Builds a picker for an event's reminders. <paramref name="eventReminders"/> is null when the
    /// event's reminders have never been synced, which reads as following the calendar — that is what
    /// Google reports for a timed event carrying none of its own, and it leaves
    /// <see cref="HasChanged"/> false so an unrelated edit sends nothing.
    /// </summary>
    /// <param name="eventReminders">The event's stored reminders, or null if never synced.</param>
    /// <param name="calendarDefault">
    /// The calendar's own default reminders, shown read-only while inheriting and copied in when
    /// inheritance is switched off. Null until a calendar-list sync has reported them, which is not
    /// the same answer as a calendar that reported having none — see
    /// <see cref="CalendarDefault"/>.
    /// </param>
    /// <param name="isAllDay">
    /// Whether the event is all-day, which decides the form the tab offers. Fixed for the model's
    /// lifetime: Google discards an event's reminders when it becomes all-day and substitutes the
    /// all-day shape rather than converting them, so toggling All day has to rebuild the picker.
    /// </param>
    public static ReminderPickerModel From(
        EventReminders? eventReminders, EventReminders? calendarDefault, bool isAllDay) =>
        new(
            eventReminders ?? EventReminders.InheritsCalendarDefault,
            // Null is kept as null rather than flattened to an empty list: the two mean different
            // things to the family, and the tab is the only place that can tell them apart.
            //
            // Only Overrides is read, and UseDefault must not be consulted — reading it would look
            // reasonable and be wrong. A calendar's defaults arrive from Google as a BARE ARRAY on a
            // calendar-list entry, which GoogleCalendarClient maps through EventReminders.Explicit,
            // so UseDefault is false for every calendar in existence and says nothing about it.
            //
            // The list is ordered for the same reason the event's own list is, and capped at the
            // per-event limit: copying more in would build a set Google would reject outright.
            calendarDefault is null
                ? null
                : InDisplayOrder(calendarDefault.Overrides)
                    .Take(EventRemindersValidator.MaxOverrides)
                    .ToList(),
            isAllDay);

    /// <summary>Whether the event is all-day, and therefore which form the tab offers.</summary>
    public bool IsAllDay { get; }

    /// <summary>
    /// The calendar's own default reminders, so the tab can say what inheriting actually does rather
    /// than showing an empty list. One nullable field carries three states, so they cannot drift
    /// apart:
    /// <list type="bullet">
    /// <item><description><c>null</c> — unknown: no calendar-list sync has reported this calendar's
    /// defaults. Not an answer, just no data yet.</description></item>
    /// <item><description>empty — the calendar has no default reminders, so following them notifies
    /// nobody.</description></item>
    /// <item><description>non-empty — the calendar's defaults, in display order and capped at the
    /// per-event limit.</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The first two have to stay apart for the same reason the event-level states do. Shown an
    /// empty panel for a calendar whose defaults are merely unknown, a family reads "no reminders";
    /// <see cref="StopUsingCalendarDefault"/> then leaves an empty list, and the save sends
    /// <c>useDefault:false</c> with an empty overrides array — Google replaces the whole reminders
    /// object, so the reminders the event was really inheriting are gone.
    /// </remarks>
    public IReadOnlyList<EventReminder>? CalendarDefault { get; }

    /// <summary>Whether the event still follows the calendar's defaults.</summary>
    public bool FollowsCalendarDefault { get; private set; }

    /// <summary>Which of Google's three states the tab is currently in.</summary>
    public ReminderPickerState State => FollowsCalendarDefault
        ? ReminderPickerState.FollowsCalendarDefault
        : _overrides.Count == 0
            ? ReminderPickerState.ExplicitlyNone
            : ReminderPickerState.Explicit;

    /// <summary>
    /// The event's own reminders, soonest first then by method. Google returns the array in an order
    /// of its own, so the picker imposes one — otherwise the list reshuffles between syncs.
    /// </summary>
    public IReadOnlyList<EventReminder> Overrides => _overrides;

    /// <summary>
    /// Whether the list is at the limit Google enforces, so the Add control has to be disabled.
    /// </summary>
    public bool IsFull => _overrides.Count >= EventRemindersValidator.MaxOverrides;

    /// <summary>
    /// Whether the tab differs from how it opened, compared as a set because Google reorders. False
    /// again once an edit is undone, so a visit that changes nothing sends nothing.
    /// </summary>
    public bool HasChanged => !ToEventReminders().SameAs(_opened);

    /// <summary>
    /// How many days before the event's first day an all-day reminder fires, clamped to
    /// <see cref="MinimumDaysBefore"/>..<see cref="MaximumDaysBefore"/>.
    /// </summary>
    /// <remarks>
    /// The clamp is the whole protection against a reminder on the day itself. "0 days before at
    /// 09:00" is an offset of <c>-540</c>, which Google does not reject — it clamps it to <c>0</c>
    /// and notifies the family at midnight. Because this cannot hold a value below one day and a
    /// <see cref="TimeOnly"/> cannot hold a whole day, the offset is positive by arithmetic rather
    /// than by validation.
    /// </remarks>
    public int DaysBefore
    {
        get => _daysBefore;
        set => Set(ref _daysBefore, Math.Clamp(value, MinimumDaysBefore, MaximumDaysBefore));
    }

    /// <summary>The local time of day an all-day reminder fires on its chosen day.</summary>
    public TimeOnly TimeOfDay
    {
        get => _timeOfDay;
        set => Set(ref _timeOfDay, value);
    }

    /// <summary>
    /// How many <see cref="Unit"/>s before a timed event starts, clamped to what that unit can
    /// express within the ceiling Google stores. Zero is a reminder at the moment the event starts,
    /// which Google accepts.
    /// </summary>
    public int Amount
    {
        get => _amount;
        set => Set(ref _amount, Math.Clamp(value, EventRemindersValidator.MinMinutes, MaximumAmount(_unit)));
    }

    /// <summary>
    /// The unit <see cref="Amount"/> is counted in. Changing it re-clamps the amount, so the pair can
    /// never describe an offset Google would rewrite.
    /// </summary>
    public ReminderUnit Unit
    {
        get => _unit;
        set
        {
            Set(ref _unit, value);
            Amount = _amount;
        }
    }

    /// <summary>
    /// How the reminder is delivered. A string because that is what Google stores and what a phone
    /// may have set; the form offers only the two values Google itself accepts on a write.
    /// </summary>
    public string Method
    {
        get => _method;
        set => Set(ref _method, value);
    }

    /// <summary>
    /// Whether the family has moved the add form away from the values it opened with — the amount
    /// and unit (or, on an all-day event, the day and time), or the delivery method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole safety property behind <see cref="CommitPendingFormReminder"/>. The form
    /// holds a value from the moment it appears, so committing it unconditionally would invent a
    /// reminder for a family who switched inheritance off <i>wanting</i> silence — which is a
    /// legitimate, supported state (<see cref="ReminderPickerState.ExplicitlyNone"/>).
    /// </para>
    /// <para>
    /// <b>Sticky, deliberately: a control moved and then moved back still counts as touched.</b>
    /// Both answers are defensible, and this one is chosen because the alternative recreates the
    /// defect it exists to fix. Somebody who dials 45 and settles on 30 has engaged with the form
    /// and is configuring a reminder, not asking for silence; reading them as untouched would
    /// discard the reminder the screen is visibly offering. Somebody who wants silence does not
    /// touch the form at all, and that case is still untouched.
    /// </para>
    /// <para>
    /// Set on an <i>effective</i> change only. Assigning a control the value it already holds is not
    /// a change — a <c>change</c> event can fire on a blur that altered nothing — and neither is an
    /// input the clamps rejected, because a value that never reached the form is not a choice the
    /// family made.
    /// </para>
    /// </remarks>
    public bool IsFormTouched => _isFormTouched;

    /// <summary>
    /// Restores inheritance, discarding the edited list. Switching back off re-copies the calendar's
    /// defaults rather than the discarded edits, which is what the Google Calendar app does.
    /// </summary>
    public void UseCalendarDefault()
    {
        FollowsCalendarDefault = true;
        _overrides.Clear();
    }

    /// <summary>
    /// Stops following the calendar and copies its defaults in as editable entries — the Google
    /// Calendar app pre-fills them rather than dropping the family onto an empty list they did not
    /// ask for. Copies nothing when the calendar's defaults are unknown, and nothing when the
    /// calendar has none; both leave an empty list, which is the state Google records as "replace
    /// the calendar's defaults with nothing".
    /// </summary>
    public void StopUsingCalendarDefault()
    {
        FollowsCalendarDefault = false;
        _overrides.Clear();
        _overrides.AddRange(InDisplayOrder(CalendarDefault ?? []));
    }

    /// <summary>
    /// Adds a reminder the kiosk is creating, or refuses it. Refused when the list is full, when the
    /// same method and offset is already set, or when either value is one Google would rewrite behind
    /// the family's back.
    /// </summary>
    /// <remarks>
    /// A duplicate is refused rather than collapsed. Google collapses it silently, but doing the same
    /// here would make Add appear to do nothing, which is indistinguishable from a broken control —
    /// and it would leave <see cref="HasChanged"/> false while the family believed they had added
    /// something.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// While the event still follows the calendar's defaults. An event either follows the calendar or
    /// replaces it: Google answers a body carrying both with
    /// <c>400 cannotUseDefaultRemindersAndSpecifyOverride</c>. Inheritance has to be switched off
    /// first, which is also what pre-fills the defaults, so reaching here is a caller bug.
    /// </exception>
    public bool TryAdd(EventReminder reminder)
    {
        if (FollowsCalendarDefault)
        {
            throw new InvalidOperationException(
                "Switch off the calendar's default reminders before adding one of the event's own.");
        }

        if (IsFull || !IsWritable(reminder) || _overrides.Contains(reminder))
        {
            return false;
        }

        _overrides.Add(reminder);
        _overrides.Sort(ByDisplayOrder);
        return true;
    }

    /// <summary>
    /// The reminder the form currently describes — days-before-at-a-time for an all-day event,
    /// amount-and-unit for a timed one — so the tab can show what Add would create before the family
    /// commits to it. <see cref="TryAddSelectedReminder"/> adds exactly this.
    /// </summary>
    public EventReminder SelectedReminder => new(Method, SelectedMinutes());

    /// <summary>
    /// Adds the reminder the form currently describes. Refused on the same grounds as
    /// <see cref="TryAdd"/>.
    /// </summary>
    public bool TryAddSelectedReminder() => TryAdd(SelectedReminder);

    /// <summary>Removes a reminder, returning whether it was there.</summary>
    public bool Remove(EventReminder reminder)
    {
        if (!_overrides.Remove(reminder))
        {
            return false;
        }

        // Remembered so no later save can put this one back — see CommitPendingFormReminder.
        _withdrawn.Add(reminder);
        return true;
    }

    /// <summary>
    /// Commits the reminder the add form describes as the event's own, for the family who configured
    /// one and saved without pressing Add. Returns whether anything was committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this exists to prevent is unasked-for silence.</b> A save that would leave an event
    /// with no reminders at all, made by somebody who had configured one on the form, is the one
    /// case where the form's pending value is the better answer: the family believe they set a
    /// reminder, nothing arrives, and nothing told them. That is the whole of the condition below,
    /// and it is why a non-empty list is left alone — there the list is the event's reminders, the
    /// family can read it, and the form stays the offer its Add button says it is.
    /// </para>
    /// <para>
    /// The save path calls this once, before it reads <see cref="HasChanged"/>. It commits through
    /// <see cref="TryAdd"/> like any other reminder, so the result is an ordinary list change and
    /// <see cref="HasChanged"/>/<see cref="ToEventReminders"/> decide what is sent exactly as before
    /// — there is no second route to the wire.
    /// </para>
    /// <para>
    /// Four conditions, and each rules out a silence the family did ask for:
    /// </para>
    /// <list type="bullet">
    /// <item><description>The form is <see cref="IsFormTouched"/>. Untouched, nobody configured
    /// anything — the form holds a value before it is touched — so switching inheritance off and
    /// saving still yields the explicitly-none state, which is how the family ask for
    /// silence.</description></item>
    /// <item><description>The event is not inheriting. While it is, the form is not even on screen,
    /// and Google answers a body carrying both an inherited and an own reminder with
    /// <c>400 cannotUseDefaultRemindersAndSpecifyOverride</c>.</description></item>
    /// <item><description>The list is empty, which is what makes the save silent. A list with
    /// something in it is the event's reminders, the family can read it, and nothing is being
    /// lost.</description></item>
    /// <item><description>The form's value is not one <see cref="Remove"/> took away in this visit.
    /// Adding a reminder and then removing it is as explicit as the tab gets about not wanting it,
    /// and handing it straight back on save would undo exactly that. Removing a <i>different</i>
    /// one and then configuring this value is a replacement, not a silencing, so it still
    /// commits.</description></item>
    /// </list>
    /// </remarks>
    public bool CommitPendingFormReminder() =>
        _isFormTouched
        && !FollowsCalendarDefault
        && _overrides.Count == 0
        && !_withdrawn.Contains(SelectedReminder)
        && TryAddSelectedReminder();

    /// <summary>
    /// The offset Google stores for "<paramref name="daysBefore"/> days before at
    /// <paramref name="timeOfDay"/>": an all-day reminder is counted backwards from local midnight on
    /// the event's first day, so the day before at 09:00 is <c>24 * 60 - 9 * 60</c>.
    /// </summary>
    public static int ToMinutesBeforeMidnight(int daysBefore, TimeOnly timeOfDay) =>
        daysBefore * MinutesPerDay - (timeOfDay.Hour * 60 + timeOfDay.Minute);

    /// <summary>
    /// Reads a stored all-day offset back as the day and time it means, so the form can show what
    /// Google holds. The inverse of <see cref="ToMinutesBeforeMidnight"/>.
    /// </summary>
    /// <remarks>
    /// Worth knowing what this exposes: 30 minutes on an all-day event is a notification at 23:30 the
    /// night before, not "half an hour before" anything the family would recognise. That is why the
    /// all-day form never shows bare minutes.
    /// </remarks>
    public static (int DaysBefore, TimeOnly TimeOfDay) ToDaysBeforeAndTime(int minutes)
    {
        // Ceiling division: any offset inside a day still belongs to that day's midnight boundary.
        var daysBefore = (minutes + MinutesPerDay - 1) / MinutesPerDay;
        var minuteOfDay = daysBefore * MinutesPerDay - minutes;
        return (daysBefore, new TimeOnly(minuteOfDay / 60, minuteOfDay % 60));
    }

    /// <summary>Converts a timed form selection to the minutes before the start Google stores.</summary>
    public static int ToMinutes(int amount, ReminderUnit unit) => amount * UnitMinutes(unit);

    /// <summary>
    /// The largest amount a unit can express without passing the ceiling Google stores, so the form's
    /// own control cannot reach a value Google would clamp.
    /// </summary>
    public static int MaximumAmount(ReminderUnit unit) =>
        EventRemindersValidator.MaxMinutes / UnitMinutes(unit);

    /// <summary>
    /// The tab's state in Google's own shape. Snapshotted, so a later keystroke cannot reach into a
    /// request already built. Only sent when <see cref="HasChanged"/> is true.
    /// </summary>
    public EventReminders ToEventReminders() => FollowsCalendarDefault
        ? EventReminders.InheritsCalendarDefault
        // An empty list yields the explicitly-none shape, which is the state the family asked for.
        : EventReminders.Explicit(_overrides);

    // One place where a form control's value lands, so no control can be added later that moves the
    // pending reminder without marking the form touched. See IsFormTouched for why an assignment
    // that changes nothing is not a touch.
    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        _isFormTouched = true;
    }

    private int SelectedMinutes() => IsAllDay
        ? ToMinutesBeforeMidnight(DaysBefore, TimeOfDay)
        : ToMinutes(Amount, Unit);

    // The values the kiosk is allowed to create. Deliberately not applied to anything Google
    // supplied — see the class remarks.
    private static bool IsWritable(EventReminder reminder) =>
        reminder.Method is EventRemindersValidator.PopupMethod or EventRemindersValidator.EmailMethod
        && reminder.Minutes >= EventRemindersValidator.MinMinutes
        && reminder.Minutes <= EventRemindersValidator.MaxMinutes;

    private static int UnitMinutes(ReminderUnit unit) => unit switch
    {
        ReminderUnit.Minutes => 1,
        ReminderUnit.Hours => 60,
        ReminderUnit.Days => MinutesPerDay,
        ReminderUnit.Weeks => 7 * MinutesPerDay,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown reminder unit.")
    };

    private static IEnumerable<EventReminder> InDisplayOrder(IEnumerable<EventReminder> reminders) =>
        reminders.OrderBy(r => r.Minutes).ThenBy(r => r.Method, StringComparer.Ordinal);

    private static int ByDisplayOrder(EventReminder left, EventReminder right)
    {
        var byMinutes = left.Minutes.CompareTo(right.Minutes);
        return byMinutes != 0 ? byMinutes : string.CompareOrdinal(left.Method, right.Method);
    }
}
