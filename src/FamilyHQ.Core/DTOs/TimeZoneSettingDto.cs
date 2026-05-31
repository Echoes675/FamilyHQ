namespace FamilyHQ.Core.DTOs;

public record TimeZoneSettingDto(string EffectiveIanaZone, bool IsExplicit, string? ExplicitIanaZone);
