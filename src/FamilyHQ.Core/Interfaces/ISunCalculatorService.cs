using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ISunCalculatorService
{
    Task<DayThemeBoundaries> CalculateBoundariesAsync(double latitude, double longitude, DateOnly date);
}
