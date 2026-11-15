using Common.Models;
using Web.Models;
using Web.Monitoring;
namespace Tests;
public class HealthSeverityTests
{
    [Theory]
    [InlineData(72,"good")]
    [InlineData(105,"watch")]
    [InlineData(120,"caution")]
    [InlineData(150,"high")]
    [InlineData(250,"critical")]
    [InlineData(33,"critical")]
    [InlineData(0,"unknown")]
    public void PulseHasGraduatedSeverity(int bpm,string expected) => Assert.Equal(expected,HealthSeverity.Pulse(bpm,"good"));
    [Fact]
    public void PoorSignalCannotBeClassifiedAsMedicalCritical() => Assert.Equal("unknown",HealthSeverity.Pulse(250,"poor"));
    [Fact]
    public void CriticalSampleDrivesWorstStatusWithoutContaminatingOtherCards()
    {
        var now=DateTimeOffset.UtcNow;
        var model=new CrewDashboardModel { Now=now,Record=new([new("sample",now,new(250,"good",null),null)],[new(now,50,100)]) };
        Assert.Equal("critical",model.State);
        Assert.All(model.Metrics,m=>Assert.Equal("critical",m.State));
        var oneWarning=new CrewDashboardModel { Now=now,Record=new([new("sample",now,new(72,"good",null),null)],[new(now,50,45)]) };
        Assert.Equal("critical",oneWarning.State);
        Assert.Equal("good",oneWarning.Metrics[2].State);
    }
    [Theory]
    [InlineData(22,"good")]
    [InlineData(28,"watch")]
    [InlineData(30,"caution")]
    [InlineData(34,"high")]
    [InlineData(50,"critical")]
    public void CabinTemperatureSeverity(double value,string expected) => Assert.Equal(expected,HealthSeverity.Temperature(value));
    [Theory]
    [InlineData(45,"good")]
    [InlineData(78,"watch")]
    [InlineData(83,"caution")]
    [InlineData(88,"high")]
    [InlineData(100,"critical")]
    [InlineData(101,"unknown")]
    public void CabinHumiditySeverity(double value,string expected) => Assert.Equal(expected,HealthSeverity.Humidity(value));
}
