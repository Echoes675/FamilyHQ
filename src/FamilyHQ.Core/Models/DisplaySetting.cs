namespace FamilyHQ.Core.Models;

public class DisplaySetting
{
    public int Id { get; set; }
    public double SurfaceMultiplier { get; set; } = 1.0;
    public bool OpaqueSurfaces { get; set; }
    public int TransitionDurationSecs { get; set; } = 15;
    public DateTimeOffset UpdatedAt { get; set; }
}
