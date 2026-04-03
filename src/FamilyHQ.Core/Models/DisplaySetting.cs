namespace FamilyHQ.Core.Models;

public class DisplaySetting
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public double SurfaceMultiplier { get; set; } = 1.0;
    public bool OpaqueSurfaces { get; set; }
    public int TransitionDurationSecs { get; set; } = 15;
    public string ThemeSelection { get; set; } = "auto";
    public DateTimeOffset UpdatedAt { get; set; }
}
