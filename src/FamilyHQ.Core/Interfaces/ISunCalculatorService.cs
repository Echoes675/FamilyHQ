using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ISunCalculatorService
{
    DayThemeBoundaries CalculateBoundaries(double latitude, double longitude, DateOnly date);
}
