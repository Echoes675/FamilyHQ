using FamilyHQ.Core.Enums;
using FamilyHQ.WebUi.Services;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Services;

public class WeatherOverrideServiceTests
{
    [Fact]
    public void Activate_SetsIsActiveAndCondition_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Activate(WeatherCondition.HeavyRain);

        sut.IsActive.Should().BeTrue();
        sut.ActiveCondition.Should().Be(WeatherCondition.HeavyRain);
        fired.Should().Be(1);
    }

    [Fact]
    public void Activate_DefaultsIsWindyToFalse()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        sut.IsWindy.Should().BeFalse();
    }

    [Fact]
    public void Activate_WhenAlreadyInSameState_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Activate(WeatherCondition.Snow); // already active with Snow + windy=false

        fired.Should().Be(0);
        sut.IsActive.Should().BeTrue();
        sut.ActiveCondition.Should().Be(WeatherCondition.Snow);
        sut.IsWindy.Should().BeFalse();
    }

    [Fact]
    public void SelectCondition_WhileActive_ReplacesCondition_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Clear);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Thunder);

        sut.ActiveCondition.Should().Be(WeatherCondition.Thunder);
        fired.Should().Be(1);
    }

    [Fact]
    public void SelectCondition_WhileInactive_IsNoOp_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Fog);

        sut.IsActive.Should().BeFalse();
        sut.ActiveCondition.Should().BeNull();
        fired.Should().Be(0);
    }

    [Fact]
    public void SelectCondition_WithSameCondition_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Cloudy);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Cloudy);

        fired.Should().Be(0);
    }

    [Fact]
    public void SetWindy_WhileActive_UpdatesFlag_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        sut.IsWindy.Should().BeTrue();
        fired.Should().Be(1);
    }

    [Fact]
    public void SetWindy_WhileInactive_IsNoOp_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        sut.IsWindy.Should().BeFalse();
        fired.Should().Be(0);
    }

    [Fact]
    public void SetWindy_WithSameValue_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Drizzle);
        sut.SetWindy(true);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        fired.Should().Be(0);
    }

    [Fact]
    public void Deactivate_ClearsCondition_ResetsIsWindy_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Sleet);
        sut.SetWindy(true);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Deactivate();

        sut.IsActive.Should().BeFalse();
        sut.ActiveCondition.Should().BeNull();
        sut.IsWindy.Should().BeFalse();
        fired.Should().Be(1);
    }

    [Fact]
    public void Deactivate_WhileInactive_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Deactivate();

        fired.Should().Be(0);
    }
}
