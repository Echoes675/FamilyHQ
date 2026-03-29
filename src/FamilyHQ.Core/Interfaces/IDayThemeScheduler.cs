namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeScheduler
{
    Task TriggerRecalculationAsync();
}
