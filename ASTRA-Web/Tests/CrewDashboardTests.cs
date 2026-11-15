using Web.Models;
using Web.Monitoring;
namespace Tests;
public class CrewDashboardTests
{
    [Theory]
    [InlineData(20,50,9.26)]
    [InlineData(25,100,25)]
    public void DewPointUsesPairedSensorValues(double t, double rh, double expected)
        => Assert.Equal(expected, CrewDashboardModel.DewPoint(t,rh)!.Value,1);
    [Theory]
    [InlineData(20,0)]
    [InlineData(20,101)]
    [InlineData(-100,50)]
    public void InvalidInputsDoNotProduceDewPoint(double t, double rh) => Assert.Null(CrewDashboardModel.DewPoint(t,rh));
    [Fact]
    public void MissingMeasurementsNeverShowHealthy()
    {
        var model = new CrewDashboardModel();
        Assert.Equal("unknown",model.State);
        Assert.True(model.Due);
        Assert.Equal(3,model.Metrics.Count);
        Assert.All(model.Metrics,m=>Assert.Equal("Not measured",m.Value));
    }
    [Theory]
    [InlineData(72,"good",0,"good")]
    [InlineData(120,"good",0,"caution")]
    [InlineData(72,"poor",0,"unknown")]
    [InlineData(72,"good",7,"unknown")]
    public void StatusIncludesQualityAndFreshness(int bpm,string quality,int age,string expected)
    {
        var now=DateTimeOffset.UtcNow;
        var model=new CrewDashboardModel { Now=now,Record=new([new("id",now.AddHours(-age),new(bpm,quality,null),null)],[new(now,22,45)]) };
        Assert.Equal(expected,model.State);
        Assert.Equal(age>=6,model.Due);
    }

    [Theory]
    [InlineData(22,45,"good")]
    [InlineData(30,45,"caution")]
    [InlineData(22,85,"caution")]
    public void CabinScreeningContributesToCombinedStatus(double temperature,double humidity,string expected)
    {
        var now=DateTimeOffset.UtcNow;
        var model=new CrewDashboardModel { Now=now,Record=new([new("id",now,new(72,"good",null),null)],[new(now,temperature,humidity)]) };
        Assert.Equal(expected,model.State);
        Assert.Equal(expected,model.CabinState);
    }
}
