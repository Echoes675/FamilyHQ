namespace FamilyHQ.Core.DTOs;

public record DayThemeDto(
    DateOnly Date,
    TimeOnly MorningStart,
    TimeOnly DaytimeStart,
    TimeOnly EveningStart,
    TimeOnly NightStart,
    string CurrentPeriod
);
