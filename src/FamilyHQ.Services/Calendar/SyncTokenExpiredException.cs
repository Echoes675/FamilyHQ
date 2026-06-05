namespace FamilyHQ.Services.Calendar;

public class SyncTokenExpiredException : Exception
{
    public SyncTokenExpiredException()
        : base("Sync token is no longer valid. Full sync required.") { }
}
