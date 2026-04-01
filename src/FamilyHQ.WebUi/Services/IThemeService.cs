namespace FamilyHQ.WebUi.Services;

public interface IThemeService : IAsyncDisposable
{
    Task InitialiseAsync();
}
