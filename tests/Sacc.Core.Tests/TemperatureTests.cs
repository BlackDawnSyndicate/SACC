using Sacc.Core;
using Xunit;

namespace Sacc.Core.Tests;

public class TemperatureTests
{
    [Fact]
    public void SeaLevelIsTheBaseTemperature() =>
        Assert.Equal(Temperature.SeaLevelCelsius, Temperature.CelsiusAt(0), 6);

    [Fact]
    public void BelowSeaLevelCountsAsSeaLevel() =>
        Assert.Equal(Temperature.SeaLevelCelsius, Temperature.CelsiusAt(-5000), 6);

    [Fact]
    public void TemperatureFallsWithAltitude()
    {
        // 1000 m of altitude at 2.5 °C/100 m drops 25 °C.
        Assert.Equal(0.0, Temperature.CelsiusAt(100_000), 6);
        Assert.True(Temperature.CelsiusAt(50_000) < Temperature.CelsiusAt(10_000));
    }
}
