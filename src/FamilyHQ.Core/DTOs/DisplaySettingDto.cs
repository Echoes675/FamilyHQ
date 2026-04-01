namespace FamilyHQ.Core.DTOs;

public record DisplaySettingDto(
    double SurfaceMultiplier,
    bool OpaqueSurfaces,
    int TransitionDurationSecs);
