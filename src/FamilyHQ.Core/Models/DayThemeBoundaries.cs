namespace FamilyHQ.Core.Models;

public record DayThemeBoundaries(
    TimeOnly MorningStart,
    TimeOnly DaytimeStart,
    TimeOnly EveningStart,
    TimeOnly NightStart
);
